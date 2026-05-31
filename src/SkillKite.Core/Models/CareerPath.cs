using SkillKite.Core.Enums;

namespace SkillKite.Core.Models;

public class CareerPath
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string TitleHi { get; set; } = string.Empty;
    public CareerCategory Category { get; set; }
    public string Description { get; set; } = string.Empty;
    public string DescriptionHi { get; set; } = string.Empty;
    public string RequirementsJson { get; set; } = "{}";
    public int SalaryRangeMin { get; set; }
    public int SalaryRangeMax { get; set; }
    public bool RemoteFriendly { get; set; }
    public DemandLevel DemandLevel { get; set; }
    public string TimeToJobReady { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    public ICollection<LearningResource> Resources { get; set; } = new List<LearningResource>();
}
