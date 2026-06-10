using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SkillKite.Core.Interfaces;
using SkillKite.Infrastructure.AI;
using SkillKite.Infrastructure.Configuration;

namespace SkillKite.API.Controllers;

[ApiController]
[Route("api/webhook/whatsapp")]
public class WebhookController : ControllerBase
{
    // In-memory dedup of WhatsApp inbound message ids. Meta retries the same
    // webhook payload if it doesn't receive our 200 in ~5s (Tier 2/3 networks
    // can be slow). Without this we'd double-process the same message — caught
    // from Shivani's 06-10 transcript where her "I will check the PDF" arrived
    // twice at 11:45:25 IST. Single-process app, so a static ConcurrentDictionary
    // is sufficient; pair each id with insertion time so we can sweep old
    // entries when the dict crosses a soft cap. WhatsApp message ids look like
    // "wamid.HBgM..." and are globally unique per send.
    private static readonly ConcurrentDictionary<string, DateTime> _seenMessageIds = new();
    private static readonly TimeSpan _seenTtl = TimeSpan.FromMinutes(10);
    private const int _seenSoftCap = 5_000;

    private readonly IMessagingService _messaging;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly WhatsAppOptions _opts;
    private readonly ILogger<WebhookController> _log;

    public WebhookController(
        IMessagingService messaging,
        IServiceScopeFactory scopeFactory,
        IOptions<WhatsAppOptions> opts,
        ILogger<WebhookController> log)
    {
        _messaging = messaging;
        _scopeFactory = scopeFactory;
        _opts = opts.Value;
        _log = log;
    }

    // WhatsApp webhook verification handshake.
    [HttpGet]
    public IActionResult Verify(
        [FromQuery(Name = "hub.mode")] string? mode,
        [FromQuery(Name = "hub.verify_token")] string? token,
        [FromQuery(Name = "hub.challenge")] string? challenge)
    {
        if (mode == "subscribe" && token == _opts.VerifyToken && challenge is not null)
            return Content(challenge, "text/plain");

        _log.LogWarning("Webhook verification failed (mode={Mode})", mode);
        return Forbid();
    }

    // Signature is validated upstream by WhatsAppSignatureValidator middleware.
    [HttpPost]
    public async Task<IActionResult> Receive(CancellationToken ct)
    {
        Request.EnableBuffering();
        using var reader = new StreamReader(Request.Body, leaveOpen: true);
        var body = await reader.ReadToEndAsync(ct);
        Request.Body.Position = 0;

        var messages = _messaging.ParseIncoming(body).ToList();

        // Drop messages we've already processed (Meta retry). Inbound id is
        // empty for status callbacks etc. — let those through unchanged since
        // ParseIncoming already filters to type=text/interactive only.
        var fresh = new List<Core.Dtos.WhatsAppIncomingMessage>(messages.Count);
        foreach (var m in messages)
        {
            if (!string.IsNullOrEmpty(m.MessageId) &&
                !_seenMessageIds.TryAdd(m.MessageId, DateTime.UtcNow))
            {
                _log.LogInformation("Dropping duplicate WhatsApp message {Id} from {From}", m.MessageId, m.From);
                continue;
            }
            fresh.Add(m);
        }
        SweepSeenIfNeeded();

        // Always return 200 fast so Meta doesn't retry. The orchestrator runs
        // in a fresh DI scope on a background task — the request scope (and
        // its scoped AppDbContext) is disposed as soon as we return.
        _ = Task.Run(async () =>
        {
            foreach (var msg in fresh)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var orchestrator = scope.ServiceProvider.GetRequiredService<AssessmentOrchestrator>();
                    await orchestrator.HandleIncomingAsync(msg.From, msg.Text, msg.ProfileName);
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Webhook processing failed for {From}", msg.From);
                }
            }
        }, CancellationToken.None);

        return Ok();
    }

    private static void SweepSeenIfNeeded()
    {
        if (_seenMessageIds.Count < _seenSoftCap) return;
        var cutoff = DateTime.UtcNow - _seenTtl;
        foreach (var kv in _seenMessageIds)
        {
            if (kv.Value < cutoff)
                _seenMessageIds.TryRemove(kv.Key, out _);
        }
    }
}
