using SkillKite.Core.Enums;

namespace SkillKite.Core.Models;

public class Student
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Phone { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? EducationLevel { get; set; }
    public string? CollegeName { get; set; }
    public int? GraduationYear { get; set; }
    public PreferredLanguage PreferredLanguage { get; set; } = PreferredLanguage.Hinglish;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastActiveAt { get; set; }

    public ICollection<ChatSession> ChatSessions { get; set; } = new List<ChatSession>();
    public ICollection<Roadmap> Roadmaps { get; set; } = new List<Roadmap>();
}
