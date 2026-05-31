using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SkillKite.Core.Enums;
using SkillKite.Core.Interfaces;
using SkillKite.Core.Models;
using SkillKite.Data;

namespace SkillKite.Infrastructure.AI;

/// <summary>
/// Coordinates the assessment lifecycle for an incoming student message:
/// upsert student, append message, run engine turn, persist state,
/// generate roadmap + PDF on completion.
/// </summary>
public class AssessmentOrchestrator
{
    private readonly AppDbContext _db;
    private readonly ICareerEngine _engine;
    private readonly IRoadmapGenerator _pdf;
    private readonly IMessagingService _messaging;
    private readonly ILogger<AssessmentOrchestrator> _log;

    public AssessmentOrchestrator(
        AppDbContext db,
        ICareerEngine engine,
        IRoadmapGenerator pdf,
        IMessagingService messaging,
        ILogger<AssessmentOrchestrator> log)
    {
        _db = db;
        _engine = engine;
        _pdf = pdf;
        _messaging = messaging;
        _log = log;
    }

    public async Task HandleIncomingAsync(string phone, string text, string? profileName, CancellationToken ct = default)
    {
        var student = await _db.Students.FirstOrDefaultAsync(s => s.Phone == phone, ct);
        if (student is null)
        {
            student = new Student { Phone = phone, Name = profileName };
            _db.Students.Add(student);
            await _db.SaveChangesAsync(ct);
        }
        student.LastActiveAt = DateTime.UtcNow;

        var session = await _db.ChatSessions
            .Include(s => s.Messages)
            .Where(s => s.StudentId == student.Id && s.Status == SessionStatus.Active)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (session is null)
        {
            session = new ChatSession { StudentId = student.Id };
            _db.ChatSessions.Add(session);
            await _db.SaveChangesAsync(ct);
        }

        _db.ChatMessages.Add(new ChatMessage
        {
            SessionId = session.Id,
            Role = MessageRole.User,
            Content = text
        });
        await _db.SaveChangesAsync(ct);

        var history = session.Messages.OrderBy(m => m.CreatedAt).ToList();

        var turn = await _engine.NextTurnAsync(session, history, text, ct);

        // Merge extracted fields into session + student.
        if (turn.ExtractedFields is { Count: > 0 })
        {
            MergeExtracted(session, student, turn.ExtractedFields);
            session.CurrentQuestionIndex = Math.Min(
                session.CurrentQuestionIndex + 1,
                AssessmentQuestions.All.Count);
        }

        _db.ChatMessages.Add(new ChatMessage
        {
            SessionId = session.Id,
            Role = MessageRole.Assistant,
            Content = turn.ReplyText
        });

        await TrySendAsync(() => _messaging.SendTextAsync(phone, turn.ReplyText, ct));

        if (turn.IsComplete)
        {
            session.Status = SessionStatus.Completed;
            session.CompletedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            // Roadmap generation (Claude) + PDF rendering takes ~15-30 seconds.
            // Send an interstitial so the user doesn't think the bot has died.
            var lang = student.PreferredLanguage;
            var waitMsg = lang == PreferredLanguage.English
                ? "🪁 Got it! Cooking up your personalized roadmap now — give me about a minute. A good plan needs a little thought!"
                : "🪁 Bas mil gaya sab kuch! Ab main aapka personalized roadmap aur PDF bana raha hoon — ek minute do mujhe. Achha plan banane mein thoda time lagta hai! 😊";
            await TrySendAsync(() => _messaging.SendTextAsync(phone, waitMsg, ct));

            try
            {
                var generated = await _engine.GenerateRoadmapAsync(student, session, ct);
                var pdfUrl = await _pdf.GenerateAsync(student, generated, ct);

                var roadmap = new Roadmap
                {
                    StudentId = student.Id,
                    TotalWeeks = generated.TotalWeeks,
                    WeeksPlanJson = JsonSerializer.Serialize(generated),
                    PdfUrl = pdfUrl
                };
                _db.Roadmaps.Add(roadmap);
                await _db.SaveChangesAsync(ct);

                var summary =
                    $"🎯 *Your career roadmap is ready!*\n\n" +
                    $"Path: {generated.CareerTitle} ({generated.CareerTitleHi})\n" +
                    $"Duration: {generated.TotalWeeks} weeks\n" +
                    $"Expected salary: ₹{generated.ExpectedSalaryMin:N0}–₹{generated.ExpectedSalaryMax:N0}/month\n\n" +
                    $"{generated.Summary}";

                await TrySendAsync(() => _messaging.SendTextAsync(phone, summary, ct));
                await TrySendAsync(() => _messaging.SendDocumentAsync(
                    phone, pdfUrl,
                    "Your SkillKite roadmap 🪁",
                    $"SkillKite_Roadmap_{student.Name ?? "student"}.pdf",
                    ct));
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Roadmap generation failed for student {Id}", student.Id);
                await TrySendAsync(() => _messaging.SendTextAsync(phone,
                    "Sorry yaar, roadmap generate karte time ek dikkat aa gayi. Thodi der baad try karenge. 🙏", ct));
            }
        }
        else
        {
            await _db.SaveChangesAsync(ct);
        }
    }

    private async Task TrySendAsync(Func<Task> send)
    {
        try { await send(); }
        catch (Exception ex)
        {
            // Local web/PWA channel uses a fake phone — WhatsApp will reject delivery.
            // The reply is already persisted in chat_messages and returned to the API caller,
            // so we swallow delivery failures rather than 500 the whole pipeline.
            _log.LogWarning(ex, "Outbound messaging failed; continuing.");
        }
    }

    private static void MergeExtracted(ChatSession session, Student student, Dictionary<string, string> extracted)
    {
        var data = string.IsNullOrWhiteSpace(session.AssessmentDataJson) || session.AssessmentDataJson == "{}"
            ? new Dictionary<string, string>()
            : JsonSerializer.Deserialize<Dictionary<string, string>>(session.AssessmentDataJson)
              ?? new Dictionary<string, string>();

        foreach (var kv in extracted)
            data[kv.Key] = kv.Value;

        session.AssessmentDataJson = JsonSerializer.Serialize(data);

        // Mirror well-known fields onto Student for easy querying.
        if (data.TryGetValue("name", out var n) && string.IsNullOrWhiteSpace(student.Name)) student.Name = n;
        if (data.TryGetValue("city", out var c) && string.IsNullOrWhiteSpace(student.City)) student.City = c;
        if (data.TryGetValue("education", out var e) && string.IsNullOrWhiteSpace(student.EducationLevel)) student.EducationLevel = e;
    }
}
