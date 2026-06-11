using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SkillKite.Infrastructure.Configuration;
using SkillKite.Infrastructure.Messaging;
using Xunit;

namespace SkillKite.Tests;

public class WhatsAppParsingTests
{
    private static WhatsAppService NewService(string appSecret = "test_secret")
    {
        var opts = Options.Create(new WhatsAppOptions
        {
            AppSecret = appSecret,
            AccessToken = "x",
            PhoneNumberId = "1",
            VerifyToken = "v"
        });
        return new WhatsAppService(
            new HttpClient(), opts, Options.Create(new PdfOptions()),
            NullLogger<WhatsAppService>.Instance);
    }

    [Fact]
    public void ParseIncoming_ExtractsTextMessage()
    {
        const string payload = """
        {
          "object": "whatsapp_business_account",
          "entry": [{
            "id": "x",
            "changes": [{
              "value": {
                "messaging_product": "whatsapp",
                "contacts": [{ "profile": { "name": "Akkyy" }, "wa_id": "919999999999" }],
                "messages": [{
                  "from": "919999999999",
                  "id": "wamid.ABC",
                  "timestamp": "1717000000",
                  "type": "text",
                  "text": { "body": "Hello SkillKite" }
                }]
              },
              "field": "messages"
            }]
          }]
        }
        """;

        var svc = NewService();
        var msgs = svc.ParseIncoming(payload).ToList();

        Assert.Single(msgs);
        Assert.Equal("919999999999", msgs[0].From);
        Assert.Equal("Hello SkillKite", msgs[0].Text);
        Assert.Equal("Akkyy", msgs[0].ProfileName);
    }

    [Fact]
    public void VerifyWebhookSignature_AcceptsCorrectHmac()
    {
        var svc = NewService("super_secret");
        const string payload = "{\"hello\":\"world\"}";
        // Pre-computed HMAC-SHA256 of payload with key "super_secret"
        using var hmac = new System.Security.Cryptography.HMACSHA256(
            System.Text.Encoding.UTF8.GetBytes("super_secret"));
        var expected = "sha256=" + Convert.ToHexString(
            hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();

        Assert.True(svc.VerifyWebhookSignature(payload, expected));
        Assert.False(svc.VerifyWebhookSignature(payload, "sha256=deadbeef"));
    }
}
