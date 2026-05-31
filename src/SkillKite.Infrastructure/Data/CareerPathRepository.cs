using Microsoft.EntityFrameworkCore;
using SkillKite.Core.Enums;
using SkillKite.Core.Interfaces;
using SkillKite.Core.Models;
using SkillKite.Data;

namespace SkillKite.Infrastructure.Data;

public class CareerPathRepository : ICareerPathRepository
{
    private readonly AppDbContext _db;
    public CareerPathRepository(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<CareerPath>> ListAsync(CareerCategory? category, CancellationToken ct = default)
    {
        var q = _db.CareerPaths.AsNoTracking().Where(c => c.IsActive);
        if (category is { } cat) q = q.Where(c => c.Category == cat);
        return await q.OrderBy(c => c.Title).ToListAsync(ct);
    }

    public Task<CareerPath?> GetAsync(Guid id, CancellationToken ct = default) =>
        _db.CareerPaths.AsNoTracking()
            .Include(c => c.Resources)
            .FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task<IReadOnlyList<LearningResource>> GetResourcesAsync(Guid careerPathId, CancellationToken ct = default) =>
        await _db.LearningResources.AsNoTracking()
            .Where(r => r.CareerPathId == careerPathId)
            .OrderBy(r => r.OrderIndex)
            .ToListAsync(ct);

    public Task<CareerPath?> FindByTitleAsync(string title, CancellationToken ct = default) =>
        _db.CareerPaths.AsNoTracking().FirstOrDefaultAsync(c => c.Title == title, ct);
}
