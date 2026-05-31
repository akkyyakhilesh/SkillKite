using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SkillKite.Core.Dtos;
using SkillKite.Core.Interfaces;
using SkillKite.Core.Models;
using SkillKite.Data;

namespace SkillKite.API.Controllers;

[ApiController]
[Route("api/roadmaps")]
public class RoadmapController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ICareerEngine _engine;
    private readonly IRoadmapGenerator _pdf;

    public RoadmapController(AppDbContext db, ICareerEngine engine, IRoadmapGenerator pdf)
    {
        _db = db;
        _engine = engine;
        _pdf = pdf;
    }

    public record GenerateRequest(Guid SessionId);

    [HttpPost("generate")]
    public async Task<IActionResult> Generate([FromBody] GenerateRequest req, CancellationToken ct)
    {
        var session = await _db.ChatSessions
            .Include(s => s.Student)
            .FirstOrDefaultAsync(s => s.Id == req.SessionId, ct);
        if (session?.Student is null) return NotFound("session not found");

        var generated = await _engine.GenerateRoadmapAsync(session.Student, session, ct);
        var pdfUrl = await _pdf.GenerateAsync(session.Student, generated, ct);

        var roadmap = new Roadmap
        {
            StudentId = session.Student.Id,
            TotalWeeks = generated.TotalWeeks,
            WeeksPlanJson = JsonSerializer.Serialize(generated),
            PdfUrl = pdfUrl
        };
        _db.Roadmaps.Add(roadmap);
        await _db.SaveChangesAsync(ct);

        return Ok(new { roadmapId = roadmap.Id, pdfUrl, roadmap = generated });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var roadmap = await _db.Roadmaps.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
        if (roadmap is null) return NotFound();

        var generated = JsonSerializer.Deserialize<GeneratedRoadmap>(roadmap.WeeksPlanJson);
        return Ok(new
        {
            roadmap.Id,
            roadmap.StudentId,
            roadmap.TotalWeeks,
            roadmap.PdfUrl,
            Status = roadmap.Status.ToString(),
            roadmap.CreatedAt,
            Plan = generated
        });
    }

    [HttpGet("{id:guid}/pdf")]
    public async Task<IActionResult> Pdf(Guid id, CancellationToken ct)
    {
        var roadmap = await _db.Roadmaps.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
        if (roadmap?.PdfUrl is null) return NotFound();
        return Redirect(roadmap.PdfUrl);
    }

    [HttpGet("by-phone/{phone}")]
    public async Task<IActionResult> ByPhone(string phone, CancellationToken ct)
    {
        var roadmaps = await _db.Roadmaps
            .AsNoTracking()
            .Where(r => r.Student!.Phone == phone)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new { r.Id, r.TotalWeeks, r.PdfUrl, r.CreatedAt, Status = r.Status.ToString() })
            .ToListAsync(ct);
        return Ok(roadmaps);
    }
}
