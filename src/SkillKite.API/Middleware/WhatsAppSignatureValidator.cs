using Microsoft.Extensions.Options;
using SkillKite.Core.Interfaces;
using SkillKite.Infrastructure.Configuration;

namespace SkillKite.API.Middleware;

/// <summary>
/// Validates the X-Hub-Signature-256 header on incoming WhatsApp webhook POSTs.
/// Bypasses GET (verify handshake) and any non-webhook routes.
/// </summary>
public class WhatsAppSignatureValidator
{
    private const string WebhookPath = "/api/webhook/whatsapp";
    private const string SignatureHeader = "X-Hub-Signature-256";

    private readonly RequestDelegate _next;
    private readonly ILogger<WhatsAppSignatureValidator> _log;

    public WhatsAppSignatureValidator(RequestDelegate next, ILogger<WhatsAppSignatureValidator> log)
    {
        _next = next;
        _log = log;
    }

    public async Task InvokeAsync(
        HttpContext ctx,
        IMessagingService messaging,
        IOptions<WhatsAppOptions> opts)
    {
        var isWebhookPost =
            HttpMethods.IsPost(ctx.Request.Method) &&
            ctx.Request.Path.StartsWithSegments(WebhookPath, StringComparison.OrdinalIgnoreCase);

        if (!isWebhookPost || string.IsNullOrEmpty(opts.Value.AppSecret))
        {
            await _next(ctx);
            return;
        }

        ctx.Request.EnableBuffering();
        using var reader = new StreamReader(ctx.Request.Body, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        ctx.Request.Body.Position = 0;

        var sig = ctx.Request.Headers[SignatureHeader].ToString();
        if (!messaging.VerifyWebhookSignature(body, sig))
        {
            _log.LogWarning("Rejected WhatsApp webhook with invalid signature");
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        await _next(ctx);
    }
}
