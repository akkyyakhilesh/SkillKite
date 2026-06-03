using SkillKite.Core.Dtos;

namespace SkillKite.Core.Interfaces;

public interface IMessagingService
{
    Task SendTextAsync(string toPhone, string text, CancellationToken ct = default);
    Task SendDocumentAsync(string toPhone, string url, string caption, string filename, CancellationToken ct = default);

    Task SendButtonsAsync(string toPhone, string body,
        IReadOnlyList<InteractiveOption> options, CancellationToken ct = default);

    Task SendListAsync(string toPhone, string body, string buttonLabel,
        string sectionTitle, IReadOnlyList<InteractiveOption> options,
        CancellationToken ct = default);

    bool VerifyWebhookSignature(string payload, string signatureHeader);
    IEnumerable<WhatsAppIncomingMessage> ParseIncoming(string payloadJson);
}
