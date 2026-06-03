using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SkillKite.Core.Dtos;
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

    // If the student finished an assessment within this window, new messages
    // are treated as post-roadmap chat rather than triggering a fresh assessment.
    // After the window we default back to offering a new assessment, but only on
    // explicit confirmation — the bug we're fixing is "any message restarts everything".
    private static readonly TimeSpan PostRoadmapWindow = TimeSpan.FromHours(24);

    // Per-phone serialization so two messages from the same student never
    // process concurrently. Without this, rapid double-replies (e.g. "Hindi"
    // then "English"), Meta webhook retry bursts after a network blip, and
    // multi-line WhatsApp splits all cause Claude to fire 2-3× in parallel
    // and produce duplicate replies / duplicate roadmaps.
    //
    // The dictionary grows monotonically with unique phones — fine at our
    // scale (~80 bytes per SemaphoreSlim, ~800 KB at 10k users). Add LRU
    // eviction later if we ever scale past that.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _phoneLocks = new();

    public async Task HandleIncomingAsync(string phone, string text, string? profileName, CancellationToken ct = default)
    {
        var sem = _phoneLocks.GetOrAdd(phone, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(ct);
        try
        {
            await HandleIncomingInternalAsync(phone, text, profileName, ct);
        }
        finally
        {
            sem.Release();
        }
    }

    private async Task HandleIncomingInternalAsync(string phone, string text, string? profileName, CancellationToken ct)
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

        // If no active session exists, check whether the student JUST completed
        // an assessment. If yes, route through post-roadmap chat instead of
        // creating a brand-new assessment session.
        if (session is null)
        {
            var recentCompleted = await _db.ChatSessions
                .Include(s => s.Messages)
                .Where(s => s.StudentId == student.Id && s.Status == SessionStatus.Completed)
                .OrderByDescending(s => s.CompletedAt)
                .FirstOrDefaultAsync(ct);

            if (recentCompleted?.CompletedAt is { } completedAt &&
                DateTime.UtcNow - completedAt < PostRoadmapWindow)
            {
                await HandlePostRoadmapAsync(student, recentCompleted, text, ct);
                return;
            }

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

        await TrySendAsync(() => SendTurnAsync(phone, turn, ct));

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

    private async Task HandlePostRoadmapAsync(
        Student student,
        ChatSession completedSession,
        string text,
        CancellationToken ct)
    {
        // Load the student's most recent generated roadmap to pass into the engine
        // so it can answer questions like "what's in week 5?" or "is this realistic?".
        var roadmapRow = await _db.Roadmaps
            .Where(r => r.StudentId == student.Id)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (roadmapRow is null)
        {
            // Edge case: completed session but no roadmap saved (generation failed).
            // Fall through to a fresh assessment so the student isn't stuck.
            var fresh = new ChatSession { StudentId = student.Id };
            _db.ChatSessions.Add(fresh);
            await _db.SaveChangesAsync(ct);
            await ContinueAssessmentAsync(student, fresh, text, ct);
            return;
        }

        GeneratedRoadmap? roadmap;
        try
        {
            roadmap = JsonSerializer.Deserialize<GeneratedRoadmap>(roadmapRow.WeeksPlanJson);
        }
        catch (JsonException ex)
        {
            _log.LogError(ex, "Could not deserialize roadmap {Id}; falling back to fresh assessment.", roadmapRow.Id);
            roadmap = null;
        }

        if (roadmap is null)
        {
            var fresh = new ChatSession { StudentId = student.Id };
            _db.ChatSessions.Add(fresh);
            await _db.SaveChangesAsync(ct);
            await ContinueAssessmentAsync(student, fresh, text, ct);
            return;
        }

        // Persist the incoming user message to the completed session — we keep
        // post-roadmap chat in the same session for full conversation continuity.
        _db.ChatMessages.Add(new ChatMessage
        {
            SessionId = completedSession.Id,
            Role = MessageRole.User,
            Content = text
        });
        await _db.SaveChangesAsync(ct);

        // Only the messages AFTER assessment completion are relevant context
        // for the post-roadmap turn — earlier turns were the assessment itself.
        var cutoff = completedSession.CompletedAt ?? DateTime.UtcNow;
        var postHistory = completedSession.Messages
            .Where(m => m.CreatedAt > cutoff)
            .OrderBy(m => m.CreatedAt)
            .ToList();

        var turn = await _engine.PostRoadmapTurnAsync(student, roadmap!, postHistory, text, ct);

        _db.ChatMessages.Add(new ChatMessage
        {
            SessionId = completedSession.Id,
            Role = MessageRole.Assistant,
            Content = turn.ReplyText
        });
        await _db.SaveChangesAsync(ct);

        await TrySendAsync(() => _messaging.SendTextAsync(student.Phone, turn.ReplyText, ct));

        if (turn.ShouldRestart)
        {
            // Student explicitly confirmed they want a fresh assessment.
            // Start a brand-new session and prime it with a fresh greeting.
            var fresh = new ChatSession { StudentId = student.Id };
            _db.ChatSessions.Add(fresh);
            await _db.SaveChangesAsync(ct);

            await ContinueAssessmentAsync(student, fresh, latestUserMessage: null, ct);
        }
    }

    /// <summary>
    /// Drive one assessment turn for an existing (possibly new) session — used
    /// when post-roadmap chat decides to restart, and as a fallback if a
    /// completed session has no recoverable roadmap.
    /// </summary>
    private async Task ContinueAssessmentAsync(
        Student student,
        ChatSession session,
        string? latestUserMessage,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(latestUserMessage))
        {
            _db.ChatMessages.Add(new ChatMessage
            {
                SessionId = session.Id,
                Role = MessageRole.User,
                Content = latestUserMessage
            });
            await _db.SaveChangesAsync(ct);
        }

        var history = session.Messages.OrderBy(m => m.CreatedAt).ToList();
        var turn = await _engine.NextTurnAsync(session, history, latestUserMessage, ct);

        _db.ChatMessages.Add(new ChatMessage
        {
            SessionId = session.Id,
            Role = MessageRole.Assistant,
            Content = turn.ReplyText
        });
        await _db.SaveChangesAsync(ct);

        await TrySendAsync(() => _messaging.SendTextAsync(student.Phone, turn.ReplyText, ct));
    }

    /// <summary>
    /// Render one assessment turn to WhatsApp. If Claude attached an
    /// InteractiveBlock (because we're asking a closed-enum question like
    /// device or salary), we send tappable buttons / a list instead of a
    /// plain text reply. The student can always still type freely.
    /// </summary>
    private async Task SendTurnAsync(string phone, AssessmentTurnResult turn, CancellationToken ct)
    {
        var block = turn.Interactive;
        if (block is null || block.Options.Count == 0)
        {
            await _messaging.SendTextAsync(phone, turn.ReplyText, ct);
            return;
        }

        // Body is the prompt shown above the options. Claude usually sets it to
        // the same line as reply; fall back to reply if it's empty.
        var body = string.IsNullOrWhiteSpace(block.Body) ? turn.ReplyText : block.Body;

        if (block.Type.Equals("list", StringComparison.OrdinalIgnoreCase))
        {
            await _messaging.SendListAsync(
                phone,
                body,
                block.ButtonLabel  ?? "Select",
                block.SectionTitle ?? "Options",
                block.Options,
                ct);
        }
        else
        {
            // Defensive: WhatsApp Reply Buttons cap at 3. If Claude over-suggested
            // (or someone added too many to AssessmentQuestions later), trim
            // gracefully so we still get something to the student.
            var btnOpts = block.Options.Count > 3 ? block.Options.Take(3).ToList() : block.Options;
            await _messaging.SendButtonsAsync(phone, body, btnOpts, ct);
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

        // Mirror well-known fields onto Student so the latest assessment is the
        // source of truth. We used to guard with IsNullOrWhiteSpace, but that
        // froze student.City / EducationLevel to the FIRST session's values
        // forever — meaning a returning student who moved cities or finished
        // their degree would get roadmaps generated against stale data
        // (e.g. recommending content writing for "10th pass in Bhagalpur" even
        // after the student says "B.Sc Zoology, Patna"). Always overwrite.
        if (data.TryGetValue("name",      out var n)) student.Name           = n;
        if (data.TryGetValue("city",      out var c)) student.City           = c;
        if (data.TryGetValue("education", out var e)) student.EducationLevel = e;

        // Roadmap language preference — drives the PDF render language.
        // Claude is instructed to normalize to "hindi" or "english"; we tolerate variants defensively.
        if (data.TryGetValue("roadmapLanguage", out var lang) && !string.IsNullOrWhiteSpace(lang))
        {
            student.PreferredLanguage = lang.Trim().ToLowerInvariant() switch
            {
                "english" or "en" or "eng" => PreferredLanguage.English,
                _                          => PreferredLanguage.Hindi,
            };
        }
    }
}
