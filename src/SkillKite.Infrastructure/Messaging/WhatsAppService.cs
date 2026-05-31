using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkillKite.Core.Dtos;
using SkillKite.Core.Interfaces;
using SkillKite.Infrastructure.Configuration;

namespace SkillKite.Infrastructure.Messaging;

public class WhatsAppService : IMessagingService
{
    private readonly HttpClient _http;
    private readonly WhatsAppOptions _opts;
    private readonly ILogger<WhatsAppService> _log;

    public WhatsAppService(HttpClient http, IOptions<WhatsAppOptions> opts, ILogger<WhatsAppService> log)
    {
        _http = http;
        _opts = opts.Value;
        _log = log;

        _http.BaseAddress = new Uri($"https://graph.facebook.com/{_opts.GraphApiVersion}/");
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _opts.AccessToken);
    }

    public async Task SendTextAsync(string toPhone, string text, CancellationToken ct = default)
    {
        var payload = new
        {
            messaging_product = "whatsapp",
            to = toPhone,
            type = "text",
            text = new { body = text }
        };
        await PostAsync(payload, ct);
    }

    public async Task SendDocumentAsync(string toPhone, string url, string caption, string filename, CancellationToken ct = default)
    {
        var payload = new
        {
            messaging_product = "whatsapp",
            to = toPhone,
            type = "document",
            document = new { link = url, caption, filename }
        };
        await PostAsync(payload, ct);
    }

    private async Task PostAsync(object payload, CancellationToken ct)
    {
        using var resp = await _http.PostAsJsonAsync($"{_opts.PhoneNumberId}/messages", payload, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            _log.LogError("WhatsApp send failed {Status}: {Body}", resp.StatusCode, body);
            resp.EnsureSuccessStatusCode();
        }
    }

    public bool VerifyWebhookSignature(string payload, string signatureHeader)
    {
        if (string.IsNullOrEmpty(_opts.AppSecret) || string.IsNullOrEmpty(signatureHeader))
            return false;

        // Header form: "sha256=<hex>"
        var expectedPrefix = "sha256=";
        if (!signatureHeader.StartsWith(expectedPrefix)) return false;
        var providedHex = signatureHeader[expectedPrefix.Length..];

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_opts.AppSecret));
        var computed = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        var computedHex = Convert.ToHexString(computed).ToLowerInvariant();

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(computedHex),
            Encoding.ASCII.GetBytes(providedHex.ToLowerInvariant()));
    }

    public IEnumerable<WhatsAppIncomingMessage> ParseIncoming(string payloadJson)
    {
        using var doc = JsonDocument.Parse(payloadJson);
        var root = doc.RootElement;

        if (!root.TryGetProperty("entry", out var entries)) yield break;

        foreach (var entry in entries.EnumerateArray())
        {
            if (!entry.TryGetProperty("changes", out var changes)) continue;
            foreach (var change in changes.EnumerateArray())
            {
                if (!change.TryGetProperty("value", out var value)) continue;

                string? profileName = null;
                if (value.TryGetProperty("contacts", out var contacts))
                {
                    var first = contacts.EnumerateArray().FirstOrDefault();
                    if (first.ValueKind == JsonValueKind.Object &&
                        first.TryGetProperty("profile", out var profile) &&
                        profile.TryGetProperty("name", out var name))
                    {
                        profileName = name.GetString();
                    }
                }

                if (!value.TryGetProperty("messages", out var messages)) continue;
                foreach (var msg in messages.EnumerateArray())
                {
                    var type = msg.TryGetProperty("type", out var t) ? t.GetString() : null;
                    if (type != "text") continue;

                    var from = msg.GetProperty("from").GetString() ?? "";
                    var id = msg.GetProperty("id").GetString() ?? "";
                    var body = msg.GetProperty("text").GetProperty("body").GetString() ?? "";
                    var tsStr = msg.TryGetProperty("timestamp", out var ts) ? ts.GetString() : null;
                    var timestamp = long.TryParse(tsStr, out var unix)
                        ? DateTimeOffset.FromUnixTimeSeconds(unix)
                        : DateTimeOffset.UtcNow;

                    yield return new WhatsAppIncomingMessage(from, id, body, profileName, timestamp);
                }
            }
        }
    }
}
