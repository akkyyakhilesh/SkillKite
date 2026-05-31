using SkillKite.Core.Enums;

namespace SkillKite.Core.Models;

public class Roadmap
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StudentId { get; set; }
    public Student? Student { get; set; }
    public Guid? CareerPathId { get; set; }
    public CareerPath? CareerPath { get; set; }

    // Structured plan: [{ weekNumber, theme, goals[], resources[{title,url}], practice }]
    public string WeeksPlanJson { get; set; } = "[]";
    public int TotalWeeks { get; set; }
    public string? PdfUrl { get; set; }
    public RoadmapStatus Status { get; set; } = RoadmapStatus.Active;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<ProgressEntry> ProgressEntries { get; set; } = new List<ProgressEntry>();
}
