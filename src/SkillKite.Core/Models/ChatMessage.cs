using SkillKite.Core.Enums;

namespace SkillKite.Core.Models;

public class ChatMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SessionId { get; set; }
    public ChatSession? Session { get; set; }
    public MessageRole Role { get; set; }   // user, assistant
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
