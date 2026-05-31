using SkillKite.Core.Enums;

namespace SkillKite.Core.Models;

public class ProgressEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RoadmapId { get; set; }
    public Roadmap? Roadmap { get; set; }
    public int WeekNumber { get; set; }
    public ProgressStatus Status { get; set; } = ProgressStatus.Pending;
    public string? Notes { get; set; }
    public string? AiFeedback { get; set; }
    public DateTime? CompletedAt { get; set; }
}
