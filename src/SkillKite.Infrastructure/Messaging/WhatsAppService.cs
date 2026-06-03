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

    /// <summary>
    /// Send an interactive Reply Buttons message (max 3 buttons). Used for
    /// closed-enum assessment questions like device, govtInterest, workType.
    /// Each button's id is what we get back in the webhook payload when the
    /// student taps it — and what Claude sees as the extracted answer.
    /// </summary>
    public async Task SendButtonsAsync(
        string toPhone, string body, IReadOnlyList<InteractiveOption> options,
        CancellationToken ct = default)
    {
        if (options.Count == 0 || options.Count > 3)
            throw new ArgumentException("WhatsApp Reply Buttons supports 1-3 buttons", nameof(options));

        var payload = new
        {
            messaging_product = "whatsapp",
            to = toPhone,
            type = "interactive",
            interactive = new
            {
                type = "button",
                body = new { text = body },
                action = new
                {
                    buttons = options.Select(o => new
                    {
                        type = "reply",
                        reply = new { id = o.Id, title = Truncate(o.Title, 20) }
                    }).ToArray()
                }
            }
        };
        await PostAsync(payload, ct);
    }

    /// <summary>
    /// Send an interactive List Message (up to 10 rows). Used when we want
    /// more options than buttons allow, or row descriptions add context
    /// (e.g. salary ranges with their "what this means" subtext).
    /// </summary>
    public async Task SendListAsync(
        string toPhone, string body, string buttonLabel,
        string sectionTitle, IReadOnlyList<InteractiveOption> options,
        CancellationToken ct = default)
    {
        if (options.Count == 0 || options.Count > 10)
            throw new ArgumentException("WhatsApp List Messages support 1-10 rows", nameof(options));

        var payload = new
        {
            messaging_product = "whatsapp",
            to = toPhone,
            type = "interactive",
            interactive = new
            {
                type = "list",
                body = new { text = body },
                action = new
                {
                    button = Truncate(buttonLabel, 20),
                    sections = new[]
                    {
                        new
                        {
                            title = Truncate(sectionTitle, 24),
                            rows = options.Select(o => new
                            {
                                id = o.Id,
                                title = Truncate(o.Title, 24),
                                description = string.IsNullOrEmpty(o.Description) ? null : Truncate(o.Description, 72)
                            }).ToArray()
                        }
                    }
                }
            }
        };
        await PostAsync(payload, ct);
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max];

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

                    var from = msg.TryGetProperty("from", out var f) ? f.GetString() ?? "" : "";
                    var id = msg.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
                    var tsStr = msg.TryGetProperty("timestamp", out var ts) ? ts.GetString() : null;
                    var timestamp = long.TryParse(tsStr, out var unix)
                        ? DateTimeOffset.FromUnixTimeSeconds(unix)
                        : DateTimeOffset.UtcNow;

                    string? body = null;

                    if (type == "text")
                    {
                        body = msg.GetProperty("text").GetProperty("body").GetString();
                    }
                    else if (type == "interactive")
                    {
                        // Button tap or list selection: flatten the chosen ID to plain text
                        // so the orchestrator + Claude can treat it identically to a typed reply.
                        // The ID is what we set when building the interactive message — so it
                        // arrives back as a clean normalized value (e.g. "phone", "full_time").
                        var interactive = msg.GetProperty("interactive");
                        var subtype = interactive.GetProperty("type").GetString();
                        if (subtype == "button_reply" &&
                            interactive.TryGetProperty("button_reply", out var br))
                        {
                            body = br.TryGetProperty("id", out var brId) ? brId.GetString() : null;
                        }
                        else if (subtype == "list_reply" &&
                                 interactive.TryGetProperty("list_reply", out var lr))
                        {
                            body = lr.TryGetProperty("id", out var lrId) ? lrId.GetString() : null;
                        }
                    }

                    if (string.IsNullOrEmpty(body)) continue;

                    yield return new WhatsAppIncomingMessage(from, id, body, profileName, timestamp);
                }
            }
        }
    }
}
