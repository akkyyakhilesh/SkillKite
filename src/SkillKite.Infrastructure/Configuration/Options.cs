namespace SkillKite.Infrastructure.Configuration;

public class ClaudeOptions
{
    public const string SectionName = "Claude";
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "claude-sonnet-4-6";
    public string BaseUrl { get; set; } = "https://api.anthropic.com/v1";
    public int MaxTokens { get; set; } = 8192;
}

public class WhatsAppOptions
{
    public const string SectionName = "WhatsApp";
    public string PhoneNumberId { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public string VerifyToken { get; set; } = string.Empty;
    public string AppSecret { get; set; } = string.Empty;
    public string GraphApiVersion { get; set; } = "v21.0";
}

public class PdfOptions
{
    public const string SectionName = "Pdf";
    public string OutputDirectory { get; set; } = "wwwroot/roadmaps";
    public string PublicBaseUrl { get; set; } = "https://localhost:7000/roadmaps";
}
