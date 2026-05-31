namespace SkillKite.Core.Models;

public class LearningResource
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CareerPathId { get; set; }
    public CareerPath? CareerPath { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;       // youtube, nptel, coursera, skillIndia
    public string Language { get; set; } = "hi";
    public bool IsFree { get; set; } = true;
    public decimal DurationHours { get; set; }
    public string SkillTagsJson { get; set; } = "[]";
    public int OrderIndex { get; set; }
}
