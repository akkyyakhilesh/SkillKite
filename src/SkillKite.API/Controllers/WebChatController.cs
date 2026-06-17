using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using SkillKite.Core.Interfaces;
using SkillKite.Data;
using SkillKite.Core.Models;
using SkillKite.Infrastructure.AI;
using SkillKite.Infrastructure.Messaging;

namespace SkillKite.API.Controllers;

[ApiController]
[Route("api/web-chat")]
[EnableCors("web")]
[EnableRateLimiting("web-chat")]
public class WebChatController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ICareerEngine _engine;
    private readonly IRoadmapGenerator _pdf;
    private readonly ILogger<AssessmentOrchestrator> _orchLogger;

    public WebChatController(
        AppDbContext db,
        ICareerEngine engine,
        IRoadmapGenerator pdf,
        ILogger<AssessmentOrchestrator> orchLogger)
    {
        _db = db;
        _engine = engine;
        _pdf = pdf;
        _orchLogger = orchLogger;
    }

    public record StartRequest(string? SessionKey);
    public record MessageRequest(string SessionKey, string Text, string? Name);

    [HttpPost("start")]
    public async Task<IActionResult> Start([FromBody] StartRequest req, CancellationToken ct)
    {
        var key = req.SessionKey;
        if (string.IsNullOrWhiteSpace(key) || key.Length > 15 || !key.StartsWith('W'))
            key = "W" + Guid.NewGuid().ToString("N")[..14];

        var student = await _db.Students.FirstOrDefaultAsync(s => s.Phone == key, ct);
        if (student is null)
        {
            student = new Student { Phone = key };
            _db.Students.Add(student);
            await _db.SaveChangesAsync(ct);
        }

        var session = await _db.ChatSessions
            .Where(s => s.StudentId == student.Id && s.Status != Core.Enums.SessionStatus.Completed && s.Status != Core.Enums.SessionStatus.Abandoned)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (session is null)
        {
            session = new ChatSession { StudentId = student.Id };
            _db.ChatSessions.Add(session);
            await _db.SaveChangesAsync(ct);
        }

        return Ok(new { sessionKey = key, sessionId = session.Id });
    }

    [HttpPost("message")]
    public async Task<IActionResult> Message([FromBody] MessageRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.SessionKey) || string.IsNullOrWhiteSpace(req.Text))
            return BadRequest("sessionKey and text required");

        if (!req.SessionKey.StartsWith('W'))
            return BadRequest("invalid sessionKey");

        var webMsg = new WebMessagingService();
        var orch = new AssessmentOrchestrator(_db, _engine, _pdf, webMsg, _orchLogger);

        await orch.HandleIncomingAsync(req.SessionKey, req.Text, req.Name, ct);

        return Ok(new { ok = true, blocks = webMsg.Buffer });
    }

    [HttpGet("history/{sessionKey}")]
    public async Task<IActionResult> History(string sessionKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sessionKey) || !sessionKey.StartsWith('W'))
            return BadRequest("invalid sessionKey");

        var student = await _db.Students
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Phone == sessionKey, ct);

        if (student is null)
            return Ok(new { sessionId = (Guid?)null, messages = Array.Empty<object>() });

        var session = await _db.ChatSessions
            .AsNoTracking()
            .Include(s => s.Messages)
            .Where(s => s.StudentId == student.Id)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (session is null)
            return Ok(new { sessionId = (Guid?)null, messages = Array.Empty<object>() });

        return Ok(new
        {
            sessionId = session.Id,
            status = session.Status.ToString(),
            messages = session.Messages
                .OrderBy(m => m.CreatedAt)
                .Select(m => new { role = m.Role.ToString(), content = m.Content, createdAt = m.CreatedAt })
        });
    }
}
