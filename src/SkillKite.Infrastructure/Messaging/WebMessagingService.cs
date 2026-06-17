using SkillKite.Core.Dtos;
using SkillKite.Core.Interfaces;

namespace SkillKite.Infrastructure.Messaging;

public class WebMessagingService : IMessagingService
{
    private readonly List<WebChatBlock> _buffer = new();
    public IReadOnlyList<WebChatBlock> Buffer => _buffer;

    public Task SendTextAsync(string toPhone, string text, CancellationToken ct = default)
    {
        _buffer.Add(new WebChatBlock("text", text, null, null, null, null));
        return Task.CompletedTask;
    }

    public Task SendButtonsAsync(string toPhone, string body,
        IReadOnlyList<InteractiveOption> options, CancellationToken ct = default)
    {
        _buffer.Add(new WebChatBlock("buttons", body, options.ToList(), null, null, null));
        return Task.CompletedTask;
    }

    public Task SendListAsync(string toPhone, string body, string buttonLabel,
        string sectionTitle, IReadOnlyList<InteractiveOption> options,
        CancellationToken ct = default)
    {
        _buffer.Add(new WebChatBlock("list", body, options.ToList(), buttonLabel, sectionTitle, null));
        return Task.CompletedTask;
    }

    public Task SendDocumentAsync(string toPhone, string url, string caption,
        string filename, CancellationToken ct = default)
    {
        _buffer.Add(new WebChatBlock("document", caption, null, null, null,
            new DocumentInfo(url, filename)));
        return Task.CompletedTask;
    }

    public bool VerifyWebhookSignature(string payload, string signatureHeader) => false;

    public IEnumerable<WhatsAppIncomingMessage> ParseIncoming(string payloadJson)
        => Enumerable.Empty<WhatsAppIncomingMessage>();
}
