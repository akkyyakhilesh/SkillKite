using SkillKite.Core.Enums;
using SkillKite.Core.Models;

namespace SkillKite.Core.Interfaces;

public interface ICareerPathRepository
{
    Task<IReadOnlyList<CareerPath>> ListAsync(CareerCategory? category, CancellationToken ct = default);
    Task<CareerPath?> GetAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<LearningResource>> GetResourcesAsync(Guid careerPathId, CancellationToken ct = default);
    Task<CareerPath?> FindByTitleAsync(string title, CancellationToken ct = default);
}
