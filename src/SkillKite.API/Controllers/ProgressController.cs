using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SkillKite.Core.Enums;
using SkillKite.Core.Models;
using SkillKite.Data;

namespace SkillKite.API.Controllers;

[ApiController]
[Route("api/progress")]
public class ProgressController : ControllerBase
{
    private readonly AppDbContext _db;

    public ProgressController(AppDbContext db) => _db = db;

    public record LogRequest(int WeekNumber, ProgressStatus Status, string? Notes);

    [HttpPost("{roadmapId:guid}")]
    public async Task<IActionResult> Log(Guid roadmapId, [FromBody] LogRequest req, CancellationToken ct)
    {
        var roadmap = await _db.Roadmaps.FirstOrDefaultAsync(r => r.Id == roadmapId, ct);
        if (roadmap is null) return NotFound();

        var entry = await _db.ProgressEntries
            .FirstOrDefaultAsync(p => p.RoadmapId == roadmapId && p.WeekNumber == req.WeekNumber, ct);

        if (entry is null)
        {
            entry = new ProgressEntry { RoadmapId = roadmapId, WeekNumber = req.WeekNumber };
            _db.ProgressEntries.Add(entry);
        }

        entry.Status = req.Status;
        entry.Notes = req.Notes;
        if (req.Status == ProgressStatus.Completed) entry.CompletedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return Ok(entry);
    }

    [HttpGet("{roadmapId:guid}")]
    public async Task<IActionResult> History(Guid roadmapId, CancellationToken ct)
    {
        var entries = await _db.ProgressEntries
            .AsNoTracking()
            .Where(p => p.RoadmapId == roadmapId)
            .OrderBy(p => p.WeekNumber)
            .Select(p => new
            {
                p.Id,
                p.WeekNumber,
                Status = p.Status.ToString(),
                p.Notes,
                p.AiFeedback,
                p.CompletedAt
            })
            .ToListAsync(ct);
        return Ok(entries);
    }
}
