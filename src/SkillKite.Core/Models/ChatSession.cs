using SkillKite.Core.Enums;

namespace SkillKite.Core.Models;

public class ChatSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StudentId { get; set; }
    public Student? Student { get; set; }
    public SessionStatus Status { get; set; } = SessionStatus.Active;

    // Structured answers as JSON (city, interests, skills, dailyHours, hasLaptop, ...)
    public string AssessmentDataJson { get; set; } = "{}";

    public int CurrentQuestionIndex { get; set; } = 0;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    public ICollection<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
}
