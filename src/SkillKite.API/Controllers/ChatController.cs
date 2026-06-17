using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SkillKite.Core.Models;
using SkillKite.Data;
using SkillKite.Infrastructure.AI;

namespace SkillKite.API.Controllers;

/// <summary>
/// Web/PWA channel — same orchestrator as WhatsApp, no telecom envelope.
/// Powers the Angular PWA in Phase 2 and is the easiest way to test
/// the engine end-to-end without registering a WhatsApp number.
/// </summary>
[ApiController]
[Route("api/chat")]
[EnableCors("web")]
public class ChatController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AssessmentOrchestrator _orchestrator;

    public ChatController(AppDbContext db, AssessmentOrchestrator orchestrator)
    {
        _db = db;
        _orchestrator = orchestrator;
    }

    public record StartRequest(string Phone, string? Name);
    public record MessageRequest(string Phone, string Text, string? Name);

    [HttpPost("start")]
    public async Task<IActionResult> Start([FromBody] StartRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Phone)) return BadRequest("phone required");

        var student = await _db.Students.FirstOrDefaultAsync(s => s.Phone == req.Phone, ct);
        if (student is null)
        {
            student = new Student { Phone = req.Phone, Name = req.Name };
            _db.Students.Add(student);
            await _db.SaveChangesAsync(ct);
        }

        var session = new ChatSession { StudentId = student.Id };
        _db.ChatSessions.Add(session);
        await _db.SaveChangesAsync(ct);

        return Ok(new { sessionId = session.Id, studentId = student.Id });
    }

    [HttpPost("message")]
    public async Task<IActionResult> Message([FromBody] MessageRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Phone) || string.IsNullOrWhiteSpace(req.Text))
            return BadRequest("phone and text required");

        await _orchestrator.HandleIncomingAsync(req.Phone, req.Text, req.Name, ct);

        // Return the assistant's latest reply so web/PWA clients don't need a second round-trip.
        var reply = await _db.ChatMessages
            .AsNoTracking()
            .Where(m => m.Session!.Student!.Phone == req.Phone &&
                        m.Role == SkillKite.Core.Enums.MessageRole.Assistant)
            .OrderByDescending(m => m.CreatedAt)
            .Select(m => m.Content)
            .FirstOrDefaultAsync(ct);

        return Ok(new { ok = true, reply });
    }

    [HttpGet("session/{id:guid}")]
    public async Task<IActionResult> Session(Guid id, CancellationToken ct)
    {
        var session = await _db.ChatSessions
            .AsNoTracking()
            .Include(s => s.Messages)
            .FirstOrDefaultAsync(s => s.Id == id, ct);
        if (session is null) return NotFound();

        return Ok(new
        {
            session.Id,
            session.StudentId,
            Status = session.Status.ToString(),
            session.AssessmentDataJson,
            session.CurrentQuestionIndex,
            session.CreatedAt,
            session.CompletedAt,
            Messages = session.Messages
                .OrderBy(m => m.CreatedAt)
                .Select(m => new { Role = m.Role.ToString(), m.Content, m.CreatedAt })
        });
    }
}
