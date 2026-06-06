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
    private readonly IMessagingService _messaging;

    public RoadmapController(
        AppDbContext db, ICareerEngine engine, IRoadmapGenerator pdf, IMessagingService messaging)
    {
        _db = db;
        _engine = engine;
        _pdf = pdf;
        _messaging = messaging;
    }

    /// <summary>
    /// Generate a roadmap for an existing session. ChosenCareerTitle is optional —
    /// when provided, Claude is constrained to that specific career (matches the
    /// "student picked one of three suggestions" flow). SendToStudent=true also
    /// delivers the PDF over WhatsApp — used as a recovery hook when a real
    /// orchestrator run timed out and never sent the document.
    /// </summary>
    public record GenerateRequest(
        Guid SessionId,
        string? ChosenCareerTitle = null,
        bool SendToStudent = false);

    [HttpPost("generate")]
    public async Task<IActionResult> Generate([FromBody] GenerateRequest req, CancellationToken ct)
    {
        var session = await _db.ChatSessions
            .Include(s => s.Student)
            .FirstOrDefaultAsync(s => s.Id == req.SessionId, ct);
        if (session?.Student is null) return NotFound("session not found");

        var generated = await _engine.GenerateRoadmapAsync(
            session.Student, session, chosenCareerTitle: req.ChosenCareerTitle, ct);
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

        bool delivered = false;
        if (req.SendToStudent && !string.IsNullOrWhiteSpace(session.Student.Phone))
        {
            try
            {
                var summary =
                    $"🎯 *Your career roadmap is ready!*\n\n" +
                    $"Path: {generated.CareerTitle} ({generated.CareerTitleHi})\n" +
                    $"Duration: {generated.TotalWeeks} weeks\n\n" +
                    $"{generated.Summary}";
                await _messaging.SendTextAsync(session.Student.Phone, summary, ct);
                await _messaging.SendDocumentAsync(
                    session.Student.Phone, pdfUrl,
                    "Your SkillKite roadmap 🪁",
                    $"SkillKite_Roadmap_{session.Student.Name ?? "student"}.pdf",
                    ct);
                delivered = true;
            }
            catch { /* roadmap row is saved either way; caller sees delivered=false */ }
        }

        return Ok(new { roadmapId = roadmap.Id, pdfUrl, delivered, roadmap = generated });
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
