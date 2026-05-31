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

        // Always return 200 fast so Meta doesn't retry. The orchestrator runs
        // in a fresh DI scope on a background task — the request scope (and
        // its scoped AppDbContext) is disposed as soon as we return.
        _ = Task.Run(async () =>
        {
            foreach (var msg in messages)
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
}
