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

        // ZEROTH check: did we just ask "do you really want to wipe everything?"
        // The reset confirm session is short-lived and transient — handle it
        // before anything else so a typed "reset" while in this state doesn't
        // re-trigger the reset prompt.
        var awaitingResetConfirm = await _db.ChatSessions
            .Include(s => s.Messages)
            .Where(s => s.StudentId == student.Id && s.Status == SessionStatus.AwaitingResetConfirm)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (awaitingResetConfirm is not null)
        {
            await HandleResetConfirmAsync(student, awaitingResetConfirm, text, ct);
            return;
        }

        // Highest-priority intent: "delete my data" / "reset" / etc. Detected
        // BEFORE any normal dispatch so it works from any state (mid-assessment,
        // post-roadmap chat, AwaitingFeedback, anywhere). Sends a 2-button
        // confirmation; actual wipe happens only after the student taps Yes.
        if (IsResetIntent(text))
        {
            await SendResetConfirmPromptAsync(student, ct);
            return;
        }

        // First check: is the student in the middle of choosing a career path?
        // (They completed assessment, the bot sent 3 suggestions, and now they're
        // tapping one of the buttons or typing a choice.)
        var awaitingChoice = await _db.ChatSessions
            .Include(s => s.Messages)
            .Where(s => s.StudentId == student.Id && s.Status == SessionStatus.AwaitingCareerChoice)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (awaitingChoice is not null)
        {
            await HandleCareerChoiceAsync(student, awaitingChoice, text, ct);
            return;
        }

        // Second check: is the student responding to a long-gap welcome-back menu?
        // (They have a previous roadmap, said hi after > 24h, we sent 3 buttons,
        // now they're tapping one.)
        var awaitingReturn = await _db.ChatSessions
            .Include(s => s.Messages)
            .Where(s => s.StudentId == student.Id && s.Status == SessionStatus.AwaitingReturnChoice)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (awaitingReturn is not null)
        {
            await HandleReturnChoiceAsync(student, awaitingReturn, text, ct);
            return;
        }

        // Third check: is the student responding to the 4-option entry menu?
        // (Brand-new student said "Hi", we sent the 10th / 12th / Career / Skill
        // upgrade list, now they're tapping one.)
        var awaitingFlow = await _db.ChatSessions
            .Include(s => s.Messages)
            .Where(s => s.StudentId == student.Id && s.Status == SessionStatus.AwaitingFlowChoice)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (awaitingFlow is not null)
        {
            await HandleFlowChoiceAsync(student, awaitingFlow, text, ct);
            return;
        }

        // Fourth check: did we just ask the student which language they want
        // before kicking off their first flow? (Only ever happens once per student.)
        var awaitingLanguage = await _db.ChatSessions
            .Include(s => s.Messages)
            .Where(s => s.StudentId == student.Id && s.Status == SessionStatus.AwaitingLanguageChoice)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (awaitingLanguage is not null)
        {
            await HandleLanguageChoiceAsync(student, awaitingLanguage, text, ct);
            return;
        }

        // Fifth check: did we just deliver a PDF and ask for feedback?
        // (Session is parked in AwaitingFeedback until they tap a button or
        // type something; either way we capture a rating and move to Completed.)
        var awaitingFeedback = await _db.ChatSessions
            .Include(s => s.Messages)
            .Where(s => s.StudentId == student.Id && s.Status == SessionStatus.AwaitingFeedback)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (awaitingFeedback is not null)
        {
            await HandleFeedbackAsync(student, awaitingFeedback, text, ct);
            return;
        }

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

            // Long-gap return: the student has a completed roadmap from > 24h ago.
            // Don't blindly start a fresh assessment — recognise them and give
            // them three sensible options before falling back to "new student" flow.
            if (recentCompleted is not null)
            {
                await SendReturnWelcomeAndAwaitChoiceAsync(student, recentCompleted, ct);
                return;
            }

            // Brand-new student: send the 4-option entry menu so they can pick
            // 10th / 12th / Career / Skill upgrade. We do NOT drop them straight
            // into the 13-question career assessment any more — that was a design
            // bug for 10th- and 12th-pass kids who want different guidance.
            await SendFlowChoiceMenuAndAwaitAsync(student, ct);
            return;
        }

        // Route by flow type. Brand-new flows (10th / 12th) have their own thin
        // state machine in this class — they don't use the 13-question engine.
        var flowType = ReadFlowType(session);
        if (flowType == "10th")
        {
            await HandleTenthTurnAsync(student, session, text, ct);
            return;
        }
        if (flowType == "12th")
        {
            await HandleTwelfthTurnAsync(student, session, text, ct);
            return;
        }
        if (flowType == "upskill")
        {
            await HandleSkillUpgradeTurnAsync(student, session, text, ct);
            return;
        }

        _db.ChatMessages.Add(new ChatMessage
        {
            SessionId = session.Id,
            Role = MessageRole.User,
            Content = text
        });
        await _db.SaveChangesAsync(ct);

        var history = session.Messages.OrderBy(m => m.CreatedAt).ToList();

        var turn = await _engine.NextTurnAsync(student, session, history, text, ct);

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
            // Assessment is done. Instead of immediately generating the roadmap,
            // we ask Claude for 3 best-fit career suggestions and let the
            // student pick one. This catches AI mismatches before we commit to
            // a 20-week plan the student won't follow.
            await SuggestCareerPathsAndAwaitChoiceAsync(student, session, ct);
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
            await StartRerollFromCompletedAsync(student, completedSession, roadmap!, ct);
        }
    }

    /// <summary>
    /// Long-gap return entry point. A student who has a completed roadmap from
    /// more than the post-roadmap window ago has just messaged us. Instead of
    /// asking them their name again (the old behaviour — felt awful for testers),
    /// recognise them and give 3 explicit options:
    ///   📖 Chat about their existing roadmap
    ///   🔄 Get fresh career options (same profile data)
    ///   🆕 Start a brand new assessment (their profile has changed)
    /// </summary>
    private async Task SendReturnWelcomeAndAwaitChoiceAsync(
        Student student, ChatSession lastCompleted, CancellationToken ct)
    {
        // We stash the previous completed session's id so the handler can route
        // "chat existing" back to it without another DB lookup.
        var data = new Dictionary<string, string>
        {
            ["previousCompletedSessionId"] = lastCompleted.Id.ToString()
        };

        var menuSession = new ChatSession
        {
            StudentId = student.Id,
            Status = SessionStatus.AwaitingReturnChoice,
            AssessmentDataJson = JsonSerializer.Serialize(data)
        };
        _db.ChatSessions.Add(menuSession);
        await _db.SaveChangesAsync(ct);

        var name = student.Name ?? "friend";
        var lang = student.PreferredLanguage;
        var body = lang == PreferredLanguage.English
            ? $"Welcome back, {name}! 🪁\n\nYou already have a SkillKite roadmap. What would you like to do today?"
            : $"Welcome back, {name}! 🪁\n\nTumhare paas already ek SkillKite roadmap hai. Kya karna chahti/chahte ho aaj?";

        // Reply button titles cap at 20 visible characters on WhatsApp — emojis
        // included. Anything longer gets silently truncated mid-word. Keep
        // labels short and respect the student's chosen language.
        var options = lang == PreferredLanguage.English
            ? new List<InteractiveOption>
            {
                new("return_chat",   "📖 Discuss roadmap"),
                new("return_reroll", "🔄 New options"),
                new("return_new",    "🆕 Fresh start")
            }
            : new List<InteractiveOption>
            {
                new("return_chat",   "📖 Purana chat"),
                new("return_reroll", "🔄 Naye options"),
                new("return_new",    "🆕 Naya plan")
            };

        await TrySendAsync(() => _messaging.SendButtonsAsync(student.Phone, body, options, ct));

        _db.ChatMessages.Add(new ChatMessage
        {
            SessionId = menuSession.Id,
            Role = MessageRole.Assistant,
            Content = body
        });
        await _db.SaveChangesAsync(ct);
    }

    private async Task HandleReturnChoiceAsync(
        Student student, ChatSession menuSession, string text, CancellationToken ct)
    {
        // Persist the user's tap/typed reply for the conversation log.
        _db.ChatMessages.Add(new ChatMessage
        {
            SessionId = menuSession.Id,
            Role = MessageRole.User,
            Content = text
        });
        await _db.SaveChangesAsync(ct);

        // Close the transient menu session so it doesn't keep catching messages.
        menuSession.Status = SessionStatus.Abandoned;
        await _db.SaveChangesAsync(ct);

        // Resolve the previously-completed session that this menu refers to.
        Guid? prevSessionId = null;
        try
        {
            using var doc = JsonDocument.Parse(menuSession.AssessmentDataJson);
            if (doc.RootElement.TryGetProperty("previousCompletedSessionId", out var idEl) &&
                idEl.ValueKind == JsonValueKind.String &&
                Guid.TryParse(idEl.GetString(), out var pid))
            {
                prevSessionId = pid;
            }
        }
        catch { /* fall through to fresh assessment */ }

        var prev = prevSessionId is null ? null : await _db.ChatSessions
            .Include(s => s.Messages)
            .FirstOrDefaultAsync(s => s.Id == prevSessionId.Value, ct);

        // Route based on the chosen option id (button taps come back as the id;
        // typed answers we tolerate by simple prefix matching).
        var choice = NormaliseReturnChoice(text);

        if (choice == "return_chat" && prev is not null)
        {
            // Resume post-roadmap chat using the OLD completed session — no
            // window check, the student explicitly asked for this.
            await HandlePostRoadmapAsync(student, prev, text: "Hi", ct);
            return;
        }

        if (choice == "return_reroll" && prev is not null)
        {
            var prevRoadmap = await LoadLatestRoadmapAsync(student.Id, ct);
            if (prevRoadmap is not null)
            {
                await StartRerollFromCompletedAsync(student, prev, prevRoadmap, ct);
                return;
            }
            // If we can't find the prior roadmap, fall through to a brand-new assessment.
        }

        // Default + explicit "Naya assessment": fresh session, but pre-seed
        // identity fields we already know from the Student row (name, city,
        // education). The engine reads AssessmentDataJson to decide what to
        // ask — without this seed it'd ask "aapka naam kya hai?" to a student
        // we've literally just greeted by name. Other answers (skills, salary
        // goal, work type, etc.) are intentionally NOT carried — that's the
        // whole point of choosing "naya assessment", the student's profile
        // may have changed.
        var seed = new Dictionary<string, string> { ["flowType"] = "career" };
        if (!string.IsNullOrWhiteSpace(student.Name))           seed["name"]      = student.Name!;
        if (!string.IsNullOrWhiteSpace(student.City))           seed["city"]      = student.City!;
        if (!string.IsNullOrWhiteSpace(student.EducationLevel)) seed["education"] = student.EducationLevel!;

        var fresh = new ChatSession
        {
            StudentId = student.Id,
            AssessmentDataJson = JsonSerializer.Serialize(seed),
            CurrentQuestionIndex = seed.Count - 1 // exclude flowType; rough hint for Claude
        };
        _db.ChatSessions.Add(fresh);
        await _db.SaveChangesAsync(ct);

        await ContinueAssessmentAsync(student, fresh, latestUserMessage: null, ct);
    }

    /// <summary>
    /// Brand-new student just said "Hi" — show the 4-option entry menu so they
    /// can pick which flow they want (10th / 12th / Career / Skill upgrade)
    /// instead of being dropped straight into the 13-question career assessment.
    ///
    /// Uses a WhatsApp List Message because we have 4 options (Reply Buttons cap at 3).
    /// </summary>
    private async Task SendFlowChoiceMenuAndAwaitAsync(Student student, CancellationToken ct)
    {
        var menuSession = new ChatSession
        {
            StudentId = student.Id,
            Status = SessionStatus.AwaitingFlowChoice
        };
        _db.ChatSessions.Add(menuSession);
        await _db.SaveChangesAsync(ct);

        var name = student.Name;
        var greet = string.IsNullOrWhiteSpace(name) ? "" : $", {name}";

        var body =
            $"Hi{greet}! 🪁 Main SkillKite hoon — aapka AI career guide.\n\n" +
            "Pehle ek baat batao — aapko kya help chahiye? Neeche se choose karo:";

        var options = new List<InteractiveOption>
        {
            new("flow_10th",
                "📚 10th ke baad",
                "Stream selection — Science / Commerce / Arts"),

            new("flow_12th",
                "🎯 12th ke baad",
                "Course selection — B.Tech / MBBS / BCA etc."),

            new("flow_career",
                "💼 Career roadmap",
                "Degree done / final year ke liye"),

            new("flow_upskill",
                "🌱 Skill upgrade",
                "Already working — next ladder rung"),
        };

        await TrySendAsync(() => _messaging.SendListAsync(
            student.Phone, body,
            buttonLabel: "Choose one",
            sectionTitle: "What do you need?",
            options, ct));

        _db.ChatMessages.Add(new ChatMessage
        {
            SessionId = menuSession.Id,
            Role = MessageRole.Assistant,
            Content = body
        });
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Student tapped a row on the 4-option entry menu (or typed an answer).
    /// Persist the choice, close the menu session, and dispatch to the
    /// appropriate downstream flow.
    /// </summary>
    private async Task HandleFlowChoiceAsync(
        Student student, ChatSession menuSession, string text, CancellationToken ct)
    {
        _db.ChatMessages.Add(new ChatMessage
        {
            SessionId = menuSession.Id,
            Role = MessageRole.User,
            Content = text
        });
        await _db.SaveChangesAsync(ct);

        menuSession.Status = SessionStatus.Abandoned;
        await _db.SaveChangesAsync(ct);

        var choice = NormaliseFlowChoice(text);

        // Validate the choice. Unknown → re-show menu so student isn't stranded.
        if (choice is not ("flow_career" or "flow_10th" or "flow_12th" or "flow_upskill"))
        {
            await SendFlowChoiceMenuAndAwaitAsync(student, ct);
            return;
        }

        // If this student has never been asked their language preference, ask it
        // ONCE — upfront, before the first real flow question — so the rest of
        // the conversation respects what they pick. Returning students who've
        // already completed a flow keep their existing preference and skip
        // straight to the chosen flow's first question.
        var isFirstTime = !await _db.ChatSessions
            .AnyAsync(s => s.StudentId == student.Id && s.Status == SessionStatus.Completed, ct);

        if (isFirstTime)
        {
            await SendLanguageChoicePromptAsync(student, choice, ct);
            return;
        }

        await StartChosenFlowAsync(student, choice, ct);
    }

    /// <summary>
    /// Helper: dispatch to the right Start*FlowAsync given a normalised flow id.
    /// Used both by HandleFlowChoiceAsync (returning users skip language ask)
    /// and HandleLanguageChoiceAsync (after a new user picks their language).
    /// </summary>
    private Task StartChosenFlowAsync(Student student, string flowChoice, CancellationToken ct) => flowChoice switch
    {
        "flow_career"  => StartCareerFlowAsync(student, ct),
        "flow_upskill" => StartSkillUpgradeFlowAsync(student, ct),
        "flow_10th"    => StartTenthFlowAsync(student, ct),
        "flow_12th"    => StartTwelfthFlowAsync(student, ct),
        _              => SendFlowChoiceMenuAndAwaitAsync(student, ct)
    };

    /// <summary>
    /// Brand-new student just picked their flow. Before they answer name /
    /// stream / field, ask which language they want the bot in. The flow they
    /// picked is stashed in the session's AssessmentDataJson under "pendingFlow"
    /// so HandleLanguageChoiceAsync can resume the right path after their tap.
    ///
    /// We use a SHORT bilingual prompt (one line English + one line Hinglish)
    /// so even a student who'd be uncomfortable in one mode can read the other.
    /// </summary>
    private async Task SendLanguageChoicePromptAsync(Student student, string pendingFlow, CancellationToken ct)
    {
        var data = new Dictionary<string, string>
        {
            ["pendingFlow"] = pendingFlow
        };

        var langSession = new ChatSession
        {
            StudentId = student.Id,
            Status = SessionStatus.AwaitingLanguageChoice,
            AssessmentDataJson = JsonSerializer.Serialize(data)
        };
        _db.ChatSessions.Add(langSession);
        await _db.SaveChangesAsync(ct);

        var body =
            "Which language should we continue in?\n" +
            "Hum kis language mein baat karein?\n\n" +
            "🤝 Hinglish — Hindi + English mix (default, most natural for most students)\n" +
            "🇬🇧 English — formal English throughout";

        var options = new List<InteractiveOption>
        {
            new("lang_hinglish", "🤝 Hinglish"),
            new("lang_english",  "🇬🇧 English")
        };

        await TrySendAsync(() => _messaging.SendButtonsAsync(student.Phone, body, options, ct));

        _db.ChatMessages.Add(new ChatMessage
        {
            SessionId = langSession.Id,
            Role = MessageRole.Assistant,
            Content = body
        });
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Student tapped a language button (or typed something). Save the
    /// preference on the Student row, abandon the transient AwaitingLanguageChoice
    /// session, and resume into the flow they originally picked.
    /// </summary>
    private async Task HandleLanguageChoiceAsync(
        Student student, ChatSession langSession, string text, CancellationToken ct)
    {
        _db.ChatMessages.Add(new ChatMessage
        {
            SessionId = langSession.Id,
            Role = MessageRole.User,
            Content = text
        });
        await _db.SaveChangesAsync(ct);

        // Default to Hinglish if the student types something other than a button id
        // ("namaste", "Hello", "Hi", emojis, etc.) — Hinglish is fine for everyone.
        var t = text.Trim().ToLowerInvariant();
        student.PreferredLanguage = t switch
        {
            "lang_english" => PreferredLanguage.English,
            "english"      => PreferredLanguage.English,
            "lang_hinglish" => PreferredLanguage.Hinglish,
            _              => PreferredLanguage.Hinglish
        };

        langSession.Status = SessionStatus.Abandoned;
        await _db.SaveChangesAsync(ct);

        // Resume into the flow that was queued before we asked for language.
        string pendingFlow = ReadField(langSession, "pendingFlow") ?? "flow_career";
        await StartChosenFlowAsync(student, pendingFlow, ct);
    }

    private async Task SendAndPersistStubAsync(
        Student student, ChatSession session, string body, CancellationToken ct)
    {
        await TrySendAsync(() => _messaging.SendTextAsync(student.Phone, body, ct));
        _db.ChatMessages.Add(new ChatMessage
        {
            SessionId = session.Id,
            Role = MessageRole.Assistant,
            Content = body
        });
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Start the existing 13-question career assessment for a student who picked
    /// "💼 Career roadmap" on the entry menu. The new session is tagged with
    /// flowType=career in AssessmentDataJson so later code (and queries) can tell
    /// which flow it belongs to without inferring from question shape.
    /// </summary>
    private async Task StartCareerFlowAsync(Student student, CancellationToken ct)
    {
        var data = new Dictionary<string, string> { ["flowType"] = "career" };
        var fresh = new ChatSession
        {
            StudentId = student.Id,
            AssessmentDataJson = JsonSerializer.Serialize(data),
            Status = SessionStatus.Active
        };
        _db.ChatSessions.Add(fresh);
        await _db.SaveChangesAsync(ct);

        await ContinueAssessmentAsync(student, fresh, latestUserMessage: null, ct);
    }

    private static string NormaliseFlowChoice(string text)
    {
        var t = text.Trim().ToLowerInvariant();
        if (t.StartsWith("flow_")) return t;

        // Tolerate common typed variants
        if (t.Contains("10th") || t.Contains("10 th") || t.Contains("class 10") || t.Contains("dasvi"))     return "flow_10th";
        if (t.Contains("12th") || t.Contains("12 th") || t.Contains("class 12") || t.Contains("barahvi"))   return "flow_12th";
        if (t.Contains("skill") || t.Contains("upgrade") || t.Contains("upskill") || t.Contains("ladder"))  return "flow_upskill";
        if (t.Contains("career") || t.Contains("roadmap") || t.Contains("degree"))                          return "flow_career";

        return "unknown";
    }

    private async Task<GeneratedRoadmap?> LoadLatestRoadmapAsync(Guid studentId, CancellationToken ct)
    {
        var roadmapRow = await _db.Roadmaps
            .Where(r => r.StudentId == studentId)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (roadmapRow is null) return null;
        try { return JsonSerializer.Deserialize<GeneratedRoadmap>(roadmapRow.WeeksPlanJson); }
        catch (JsonException) { return null; }
    }

    private static string NormaliseReturnChoice(string text)
    {
        var t = text.Trim().ToLowerInvariant();
        if (t.StartsWith("return_")) return t;
        // Tolerate common typed variants
        if (t.Contains("baat") || t.Contains("question") || t.Contains("chat") || t.Contains("review")) return "return_chat";
        if (t.Contains("naya") && !t.Contains("assess")) return "return_reroll";
        if (t.Contains("fresh") || t.Contains("alternat")) return "return_reroll";
        if (t.Contains("assess") || t.Contains("update") || t.Contains("change")) return "return_new";
        return "return_new";  // safest default — when in doubt, ask fresh
    }

    /// <summary>
    /// Returning student wants a new roadmap. We do NOT make them redo the 13-question
    /// assessment — that was the painful UX testers flagged. Instead:
    ///   1. Carry forward their existing AssessmentDataJson (name, city, skills, etc.).
    ///   2. Record the previous career so the suggestion prompt can offer different alternatives.
    ///   3. Send a warm "welcome back, generating fresh options" message.
    ///   4. Jump straight to the 3-career suggestion step.
    /// The student gets new options in under 30 seconds without retyping a single answer.
    /// </summary>
    private async Task StartRerollFromCompletedAsync(
        Student student,
        ChatSession completedSession,
        GeneratedRoadmap previousRoadmap,
        CancellationToken ct)
    {
        // Carry forward known answers + stash the previous career so the suggestion
        // prompt knows to offer different options.
        var carriedData = string.IsNullOrWhiteSpace(completedSession.AssessmentDataJson) ||
                          completedSession.AssessmentDataJson == "{}"
            ? new Dictionary<string, string>()
            : JsonSerializer.Deserialize<Dictionary<string, string>>(completedSession.AssessmentDataJson)
              ?? new Dictionary<string, string>();
        carriedData.Remove("suggestedCareers"); // stale — about to be regenerated
        carriedData["previousCareer"] = previousRoadmap.CareerTitle ?? "";

        var fresh = new ChatSession
        {
            StudentId = student.Id,
            AssessmentDataJson = JsonSerializer.Serialize(carriedData),
            CurrentQuestionIndex = AssessmentQuestions.All.Count, // already "answered" via carry-forward
            Status = SessionStatus.Active
        };
        _db.ChatSessions.Add(fresh);
        await _db.SaveChangesAsync(ct);

        var lang = student.PreferredLanguage;
        var welcomeMsg = lang == PreferredLanguage.English
            ? $"Welcome back, {student.Name ?? "friend"}! 🪁 I remember your details — let me pick 3 fresh career options for you. Give me a moment…"
            : $"Welcome back {student.Name ?? "friend"}! 🪁 Tumhari details yaad hain mujhe — 3 fresh career options nikal raha hoon. Ek minute do…";

        await TrySendAsync(() => _messaging.SendTextAsync(student.Phone, welcomeMsg, ct));

        _db.ChatMessages.Add(new ChatMessage
        {
            SessionId = fresh.Id,
            Role = MessageRole.Assistant,
            Content = welcomeMsg
        });
        await _db.SaveChangesAsync(ct);

        // Skip the assessment entirely — go straight to fresh suggestions.
        await SuggestCareerPathsAndAwaitChoiceAsync(student, fresh, ct);
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
        var turn = await _engine.NextTurnAsync(student, session, history, latestUserMessage, ct);

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
    /// Called when the assessment has just been completed. Pulls 3 career
    /// suggestions from Claude, sends them as 3 Reply Buttons (with the
    /// rationale for each in the message body), and parks the session in
    /// <see cref="SessionStatus.AwaitingCareerChoice"/> so the next inbound
    /// message can be routed to the choice handler.
    /// </summary>
    private async Task SuggestCareerPathsAndAwaitChoiceAsync(
        Student student, ChatSession session, CancellationToken ct)
    {
        CareerSuggestionsResult suggestions;
        try
        {
            suggestions = await _engine.SuggestCareerPathsAsync(student, session, ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Career suggestion failed for student {Id}; falling back to direct roadmap.", student.Id);
            // Defensive fallback: if suggestions fail for any reason, go straight to
            // the legacy single-roadmap path so the student isn't stranded.
            await GenerateAndDeliverRoadmapAsync(student, session, chosenCareerTitle: null, ct);
            return;
        }

        // Persist the suggestions in the session so we can resolve the chosen ID
        // back to a title when the student's selection comes in.
        var data = MergeIntoAssessmentData(session, "suggestedCareers",
            JsonSerializer.Serialize(suggestions.Suggestions));
        session.AssessmentDataJson = data;
        session.Status = SessionStatus.AwaitingCareerChoice;
        await _db.SaveChangesAsync(ct);

        // Body holds the intro + the per-career rationale (so the student sees
        // the AI's reasoning before picking). The 3 buttons hold just the titles.
        var bodyLines = new List<string> { suggestions.IntroLine, "" };
        var letter = (char)('A');
        foreach (var s in suggestions.Suggestions)
        {
            bodyLines.Add($"{letter}. {s.Title}");
            bodyLines.Add($"   — {s.Rationale}");
            bodyLines.Add("");
            letter++;
        }
        bodyLines.Add("Choose one to continue 👇");

        var options = suggestions.Suggestions
            .Take(3)
            .Select(s => new InteractiveOption(s.Id, Truncate(s.Title, 20)))
            .ToList();

        await TrySendAsync(() => _messaging.SendButtonsAsync(
            student.Phone, string.Join("\n", bodyLines), options, ct));

        _db.ChatMessages.Add(new ChatMessage
        {
            SessionId = session.Id,
            Role = MessageRole.Assistant,
            Content = string.Join("\n", bodyLines)
        });
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Called when the student has tapped a career suggestion button (or typed
    /// the title). Resolves the choice, runs the existing roadmap generation
    /// pipeline with that career as a constraint, and delivers the PDF.
    /// </summary>
    private async Task HandleCareerChoiceAsync(
        Student student, ChatSession session, string text, CancellationToken ct)
    {
        // Persist the user message so the conversation log is complete.
        _db.ChatMessages.Add(new ChatMessage
        {
            SessionId = session.Id,
            Role = MessageRole.User,
            Content = text
        });
        await _db.SaveChangesAsync(ct);

        // Resolve the student's text to a CareerSuggestion: try matching the
        // selected button id first, then a case-insensitive title prefix, then
        // give up and pass the raw text through as the chosen career title.
        var stored = TryGetStoredSuggestions(session);
        var chosen = stored?.FirstOrDefault(s =>
                         string.Equals(s.Id, text.Trim(), StringComparison.OrdinalIgnoreCase))
                     ?? stored?.FirstOrDefault(s =>
                         s.Title.StartsWith(text.Trim(), StringComparison.OrdinalIgnoreCase));

        var chosenTitle = chosen?.Title ?? text.Trim();
        await GenerateAndDeliverRoadmapAsync(student, session, chosenTitle, ct);
    }

    private IReadOnlyList<CareerSuggestion>? TryGetStoredSuggestions(ChatSession session)
    {
        try
        {
            using var doc = JsonDocument.Parse(session.AssessmentDataJson);
            if (!doc.RootElement.TryGetProperty("suggestedCareers", out var s) ||
                s.ValueKind != JsonValueKind.String) return null;
            return JsonSerializer.Deserialize<List<CareerSuggestion>>(s.GetString() ?? "[]");
        }
        catch { return null; }
    }

    private static string MergeIntoAssessmentData(ChatSession session, string key, string value)
    {
        var data = string.IsNullOrWhiteSpace(session.AssessmentDataJson) || session.AssessmentDataJson == "{}"
            ? new Dictionary<string, string>()
            : JsonSerializer.Deserialize<Dictionary<string, string>>(session.AssessmentDataJson)
              ?? new Dictionary<string, string>();
        data[key] = value;
        return JsonSerializer.Serialize(data);
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max];

    /// <summary>
    /// Existing roadmap-gen + PDF + WhatsApp delivery pipeline. Extracted from
    /// the old inline block so both the new "student chose a career" path and
    /// the fallback path can call it.
    /// </summary>
    private async Task GenerateAndDeliverRoadmapAsync(
        Student student, ChatSession session, string? chosenCareerTitle, CancellationToken ct)
    {
        var lang = student.PreferredLanguage;
        var waitMsg = lang == PreferredLanguage.English
            ? "🪁 Got it! Cooking up your personalized roadmap now — give me about a minute. A good plan needs a little thought!"
            : "🪁 Bas mil gaya sab kuch! Ab main aapka personalized roadmap aur PDF bana raha hoon — ek minute do mujhe. Achha plan banane mein thoda time lagta hai! 😊";
        await TrySendAsync(() => _messaging.SendTextAsync(student.Phone, waitMsg, ct));

        try
        {
            var generated = await _engine.GenerateRoadmapAsync(student, session, chosenCareerTitle, ct);
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

            await TrySendAsync(() => _messaging.SendTextAsync(student.Phone, summary, ct));
            await TrySendAsync(() => _messaging.SendDocumentAsync(
                student.Phone, pdfUrl,
                "Your SkillKite roadmap 🪁",
                $"SkillKite_Roadmap_{student.Name ?? "student"}.pdf",
                ct));

            // PDF delivered — park session in AwaitingFeedback and send the
            // 3-button rating prompt. Old behaviour (immediately marking
            // Completed before generation) hid silent failures; that's gone.
            await SendFeedbackPromptAsync(student, session, ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Roadmap generation failed for student {Id}", student.Id);
            session.Status = SessionStatus.Abandoned;
            await _db.SaveChangesAsync(ct);
            await TrySendAsync(() => _messaging.SendTextAsync(student.Phone,
                "Sorry yaar, roadmap generate karte time ek dikkat aa gayi. Thodi der baad try karenge. 🙏", ct));
        }
    }

    // ============================================================================
    // 10th flow — thin discovery state machine.
    //
    // Three steps stored in AssessmentDataJson under "step":
    //   "name"      → bot asked the student's name, awaiting free-text reply
    //   "interest"  → bot showed the 5-row interest list, awaiting tap/typed reply
    //   "goal"      → bot showed the 3-button goal prompt, awaiting tap/typed reply
    //
    // After "goal" we call Claude → render PDF → send PDF → mark session Completed.
    // No 13-question assessment, no career-suggestion loop — that's by design.
    // ============================================================================

    private async Task StartTenthFlowAsync(Student student, CancellationToken ct)
    {
        var data = new Dictionary<string, string>
        {
            ["flowType"] = "10th",
            ["step"]     = "name"
        };
        var session = new ChatSession
        {
            StudentId = student.Id,
            AssessmentDataJson = JsonSerializer.Serialize(data),
            Status = SessionStatus.Active
        };
        _db.ChatSessions.Add(session);
        await _db.SaveChangesAsync(ct);

        var ask = student.PreferredLanguage == PreferredLanguage.English
            ? "Great! 📚 To suggest the best options after 10th, I'd like to know a few things about you.\n\nFirst — *what's your name?*"
            : "Bahut accha! 📚 10th ke baad ke options dekhne ke liye thoda tumhare baare mein janna chahta hoon.\n\nPehle batao — *aapka naam kya hai?*";
        await TrySendAsync(() => _messaging.SendTextAsync(student.Phone, ask, ct));
        _db.ChatMessages.Add(new ChatMessage
        {
            SessionId = session.Id,
            Role = MessageRole.Assistant,
            Content = ask
        });
        await _db.SaveChangesAsync(ct);
    }

    private async Task HandleTenthTurnAsync(Student student, ChatSession session, string text, CancellationToken ct)
    {
        // Persist user message
        _db.ChatMessages.Add(new ChatMessage
        {
            SessionId = session.Id,
            Role = MessageRole.User,
            Content = text
        });
        await _db.SaveChangesAsync(ct);

        var step = ReadField(session, "step") ?? "name";

        switch (step)
        {
            case "name":
            {
                var name = text.Trim();
                if (string.IsNullOrWhiteSpace(name) || name.Length > 60)
                    name = student.Name ?? "friend";
                student.Name = name;
                session.AssessmentDataJson = WriteField(session, ("name", name), ("step", "interest"));
                await _db.SaveChangesAsync(ct);
                await SendTenthInterestPromptAsync(student, session, ct);
                return;
            }
            case "interest":
            {
                var interest = NormaliseTenthInterest(text);
                session.AssessmentDataJson = WriteField(session, ("interest", interest), ("step", "goal"));
                await _db.SaveChangesAsync(ct);
                await SendTenthGoalPromptAsync(student, session, interest, ct);
                return;
            }
            case "goal":
            {
                var goal = NormaliseGoal(text);
                session.AssessmentDataJson = WriteField(session, ("goal", goal), ("step", "generating"));
                await _db.SaveChangesAsync(ct);
                await DeliverTenthGuideAsync(student, session, ct);
                return;
            }
            default:
                // Already generating or post-complete — just acknowledge.
                await TrySendAsync(() => _messaging.SendTextAsync(student.Phone,
                    "Aapki guide ban rahi hai 🪁 thoda ruko…", ct));
                return;
        }
    }

    private async Task SendTenthInterestPromptAsync(Student student, ChatSession session, CancellationToken ct)
    {
        var name = student.Name ?? "friend";
        var body = $"Nice to meet you, {name}! 🙌\n\nAapko *kis cheez mein interest* hai? Neeche se choose karo:";
        var options = new List<InteractiveOption>
        {
            new("science_medical", "🩺 Science / Medical",  "Doctor, Nurse, Lab tech banna hai"),
            new("science_maths",   "🔢 Science / Maths",    "Engineer, IT, Computer interest hai"),
            new("commerce",        "📊 Commerce",            "Business, Accounting, CA"),
            new("arts",            "🎨 Arts / Humanities",   "Teacher, Lawyer, Govt job, Writing"),
            new("confused",        "🤔 Fix nahi hai",        "Confused — sab options dikhao")
        };
        await TrySendAsync(() => _messaging.SendListAsync(
            student.Phone, body, "Choose one", "Aapka interest", options, ct));

        _db.ChatMessages.Add(new ChatMessage
        {
            SessionId = session.Id,
            Role = MessageRole.Assistant,
            Content = body
        });
        await _db.SaveChangesAsync(ct);
    }

    private async Task SendTenthGoalPromptAsync(Student student, ChatSession session, string interest, CancellationToken ct)
    {
        var body = "Got it 👍\n\nAap *aage padhna* chahte ho ya *abhi earning start* karni hai?";
        var options = new List<InteractiveOption>
        {
            new("study", "📖 Padhna hai"),
            new("earn",  "💰 Earning start"),
            new("both",  "🤔 Dono jaanna hai")
        };
        await TrySendAsync(() => _messaging.SendButtonsAsync(student.Phone, body, options, ct));
        _db.ChatMessages.Add(new ChatMessage
        {
            SessionId = session.Id,
            Role = MessageRole.Assistant,
            Content = body
        });
        await _db.SaveChangesAsync(ct);
    }

    private async Task DeliverTenthGuideAsync(Student student, ChatSession session, CancellationToken ct)
    {
        var wait = "Bas mil gaya sab kuch! 🪁 Aapki personalized guide ban rahi hai — ek minute do mujhe.";
        await TrySendAsync(() => _messaging.SendTextAsync(student.Phone, wait, ct));
        _db.ChatMessages.Add(new ChatMessage
        {
            SessionId = session.Id,
            Role = MessageRole.Assistant,
            Content = wait
        });
        await _db.SaveChangesAsync(ct);

        try
        {
            var guide = await _engine.GenerateTenthGuideAsync(student, session, ct);
            var pdfUrl = await _pdf.GenerateGuideAsync(student, guide, ct);

            var summary = $"🎯 *Aapki 10th-ke-baad guide ready hai!*\n\n{guide.Greeting}\n\nPDF mein saare options labelled hain — padh kar parents/teachers se discuss karo.";
            await TrySendAsync(() => _messaging.SendTextAsync(student.Phone, summary, ct));
            await TrySendAsync(() => _messaging.SendDocumentAsync(
                student.Phone, pdfUrl,
                "Your SkillKite 10th guide 🪁",
                $"SkillKite_10th_Guide_{student.Name ?? "student"}.pdf",
                ct));

            _db.ChatMessages.Add(new ChatMessage
            {
                SessionId = session.Id,
                Role = MessageRole.Assistant,
                Content = summary
            });
            await _db.SaveChangesAsync(ct);

            // PDF delivered → ask for feedback. Session moves to AwaitingFeedback.
            await SendFeedbackPromptAsync(student, session, ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "10th-flow guide generation failed for student {Id}", student.Id);
            session.Status = SessionStatus.Abandoned;
            await _db.SaveChangesAsync(ct);
            await TrySendAsync(() => _messaging.SendTextAsync(student.Phone,
                "Sorry yaar, guide generate karte time ek dikkat aa gayi. Thodi der baad try karenge. 🙏", ct));
        }
    }

    private static string NormaliseTenthInterest(string text)
    {
        var t = text.Trim().ToLowerInvariant();
        if (t is "science_medical" or "science_maths" or "commerce" or "arts" or "confused") return t;
        if (t.Contains("medical") || t.Contains("doctor") || t.Contains("nurse") || t.Contains("bio")) return "science_medical";
        if (t.Contains("math") || t.Contains("engineer") || t.Contains("comput") || t.Contains("pcm"))  return "science_maths";
        if (t.Contains("commerce") || t.Contains("ca ") || t.Contains("business") || t.Contains("account")) return "commerce";
        if (t.Contains("arts") || t.Contains("humanit") || t.Contains("teach") || t.Contains("law"))    return "arts";
        return "confused";
    }

    private static string NormaliseGoal(string text)
    {
        var t = text.Trim().ToLowerInvariant();
        if (t is "study" or "earn" or "both") return t;
        if (t.Contains("padh") || t.Contains("study"))   return "study";
        if (t.Contains("earn") || t.Contains("kamai") || t.Contains("job") || t.Contains("paisa")) return "earn";
        if (t.Contains("both") || t.Contains("dono"))    return "both";
        return "both";
    }

    // ============================================================================
    // 12th flow — same thin-discovery pattern as 10th, but stream-aware Q4.
    //
    // Steps stored in AssessmentDataJson under "step":
    //   "name"      → bot asked the student's name
    //   "stream"    → bot showed the 5-row stream list (PCM/PCB/Commerce/Arts/BBA)
    //   "goal"      → bot showed the 3-button goal prompt (study/earn/both)
    //   "direction" → ONLY if goal=study|both — bot showed stream-specific
    //                 direction list. If goal=earn we skip this step entirely.
    //
    // After the last applicable step we call Claude → render PDF → send PDF.
    // ============================================================================

    private async Task StartTwelfthFlowAsync(Student student, CancellationToken ct)
    {
        var data = new Dictionary<string, string>
        {
            ["flowType"] = "12th",
            ["step"]     = "name"
        };
        var session = new ChatSession
        {
            StudentId = student.Id,
            AssessmentDataJson = JsonSerializer.Serialize(data),
            Status = SessionStatus.Active
        };
        _db.ChatSessions.Add(session);
        await _db.SaveChangesAsync(ct);

        var ask = student.PreferredLanguage == PreferredLanguage.English
            ? "Great! 🎯 To suggest the best options after 12th, I'd like to know a few things about you.\n\nFirst — *what's your name?*"
            : "Bahut accha! 🎯 12th ke baad ke options dekhne ke liye thoda tumhare baare mein janna chahta hoon.\n\nPehle batao — *aapka naam kya hai?*";
        await TrySendAsync(() => _messaging.SendTextAsync(student.Phone, ask, ct));
        _db.ChatMessages.Add(new ChatMessage
        {
            SessionId = session.Id,
            Role = MessageRole.Assistant,
            Content = ask
        });
        await _db.SaveChangesAsync(ct);
    }

    private async Task HandleTwelfthTurnAsync(Student student, ChatSession session, string text, CancellationToken ct)
    {
        _db.ChatMessages.Add(new ChatMessage
        {
            SessionId = session.Id,
            Role = MessageRole.User,
            Content = text
        });
        await _db.SaveChangesAsync(ct);

        var step = ReadField(session, "step") ?? "name";

        switch (step)
        {
            case "name":
            {
                var name = text.Trim();
                if (string.IsNullOrWhiteSpace(name) || name.Length > 60)
                    name = student.Name ?? "friend";
                student.Name = name;
                session.AssessmentDataJson = WriteField(session, ("name", name), ("step", "stream"));
                await _db.SaveChangesAsync(ct);
                await SendTwelfthStreamPromptAsync(student, session, ct);
                return;
            }
            case "stream":
            {
                var stream = NormaliseStream(text);
                session.AssessmentDataJson = WriteField(session, ("stream", stream), ("step", "goal"));
                await _db.SaveChangesAsync(ct);
                await SendTwelfthGoalPromptAsync(student, session, ct);
                return;
            }
            case "goal":
            {
                var goal = NormaliseGoal(text);
                if (goal == "earn")
                {
                    // Skip Q4 — no direction needed if they're not studying further.
                    session.AssessmentDataJson = WriteField(session,
                        ("goal", goal), ("direction", "not_sure"), ("step", "generating"));
                    await _db.SaveChangesAsync(ct);
                    await DeliverTwelfthGuideAsync(student, session, ct);
                    return;
                }
                session.AssessmentDataJson = WriteField(session, ("goal", goal), ("step", "direction"));
                await _db.SaveChangesAsync(ct);
                var stream = ReadField(session, "stream") ?? "not_sure";
                await SendTwelfthDirectionPromptAsync(student, session, stream, ct);
                return;
            }
            case "direction":
            {
                var stream = ReadField(session, "stream") ?? "not_sure";
                var direction = NormaliseDirection(stream, text);
                session.AssessmentDataJson = WriteField(session,
                    ("direction", direction), ("step", "generating"));
                await _db.SaveChangesAsync(ct);
                await DeliverTwelfthGuideAsync(student, session, ct);
                return;
            }
            default:
                await TrySendAsync(() => _messaging.SendTextAsync(student.Phone,
                    "Aapki guide ban rahi hai 🪁 thoda ruko…", ct));
                return;
        }
    }

    private async Task SendTwelfthStreamPromptAsync(Student student, ChatSession session, CancellationToken ct)
    {
        var name = student.Name ?? "friend";
        var body = $"Nice to meet you, {name}! 🙌\n\nAapne 12th mein *kaunsa stream* liya tha? Neeche se choose karo:";
        var options = new List<InteractiveOption>
        {
            new("pcm",      "🔢 PCM",       "Science with Maths"),
            new("pcb",      "🧬 PCB",       "Science with Biology"),
            new("commerce", "📊 Commerce",  "B.Com, CA, BBA"),
            new("arts",     "📖 Arts",      "Humanities, Law, UPSC"),
            new("bba",      "💼 BBA / Voc", "Vocational / BBA stream")
        };
        await TrySendAsync(() => _messaging.SendListAsync(
            student.Phone, body, "Choose one", "Aapka stream", options, ct));

        _db.ChatMessages.Add(new ChatMessage
        {
            SessionId = session.Id, Role = MessageRole.Assistant, Content = body
        });
        await _db.SaveChangesAsync(ct);
    }

    private async Task SendTwelfthGoalPromptAsync(Student student, ChatSession session, CancellationToken ct)
    {
        var body = "Got it 👍\n\nAap *aage padhna* chahte ho ya *job/earning start* karni hai?";
        var options = new List<InteractiveOption>
        {
            new("study", "📖 Padhna hai"),
            new("earn",  "💰 Job / earning"),
            new("both",  "🤔 Dono jaanna hai")
        };
        await TrySendAsync(() => _messaging.SendButtonsAsync(student.Phone, body, options, ct));
        _db.ChatMessages.Add(new ChatMessage
        {
            SessionId = session.Id, Role = MessageRole.Assistant, Content = body
        });
        await _db.SaveChangesAsync(ct);
    }

    private async Task SendTwelfthDirectionPromptAsync(
        Student student, ChatSession session, string stream, CancellationToken ct)
    {
        var body = "Aapke mann mein koi *specific direction* hai? Neeche se choose karo (ya 'sab dikhao' bolo):";
        var options = stream switch
        {
            "pcm" => new List<InteractiveOption>
            {
                new("engineering",  "🛠️ Engineering",   "B.Tech / BE"),
                new("pure_science", "🔬 Pure Science",  "B.Sc Physics/Maths"),
                new("bca",          "💻 BCA",            "Computer Applications"),
                new("architecture", "🏛️ Architecture",  "B.Arch via NATA"),
                new("defence",      "🪖 Defence (NDA)", "Army / Navy / Air Force"),
                new("not_sure",     "🤷 Sab dikhao",    "Not sure — show all")
            },
            "pcb" => new List<InteractiveOption>
            {
                new("medical",     "🩺 Medical (MBBS/BDS)", "NEET wala route"),
                new("paramedical", "💊 Paramedical",         "Nursing, BPT, BMLT"),
                new("pharmacy",    "💉 Pharmacy",            "B.Pharm"),
                new("pure_science","🔬 Pure Science",        "B.Sc Bio / Biotech"),
                new("not_sure",    "🤷 Sab dikhao",          "Not sure — show all")
            },
            "commerce" => new List<InteractiveOption>
            {
                new("ca_cs_cma", "📒 CA / CS / CMA", "Professional courses"),
                new("bcom",      "📊 B.Com / Hons", "Most common route"),
                new("bba",       "💼 BBA / Mgmt",    "Management track"),
                new("law",       "⚖️ Law (B.Com LLB)", "Integrated law"),
                new("banking",   "🏦 Banking / Finance", "BFSI sector"),
                new("not_sure",  "🤷 Sab dikhao",   "Not sure — show all")
            },
            "arts" => new List<InteractiveOption>
            {
                new("law",       "⚖️ Law (BA LLB / CLAT)", "Integrated law"),
                new("upsc",      "🇮🇳 Govt job prep",       "UPSC / SSC / PSC"),
                new("mass_comm", "📰 Mass Comm / Media",   "Journalism / PR"),
                new("design",    "🎨 Design (NID/NIFT)",   "B.Des track"),
                new("ba_hons",   "📚 BA / BA Hons",        "General degree"),
                new("not_sure",  "🤷 Sab dikhao",          "Not sure — show all")
            },
            "bba" => new List<InteractiveOption>
            {
                new("mba",            "🎯 MBA track",         "CAT/MAT/XAT route"),
                new("entrepreneurship","🚀 Entrepreneurship", "Apna startup"),
                new("not_sure",       "🤷 Sab dikhao",        "Not sure — show all")
            },
            _ => new List<InteractiveOption>
            {
                new("not_sure", "🤷 Sab dikhao", "Show me all options")
            }
        };

        await TrySendAsync(() => _messaging.SendListAsync(
            student.Phone, body, "Choose one", "Direction", options, ct));

        _db.ChatMessages.Add(new ChatMessage
        {
            SessionId = session.Id, Role = MessageRole.Assistant, Content = body
        });
        await _db.SaveChangesAsync(ct);
    }

    private async Task DeliverTwelfthGuideAsync(Student student, ChatSession session, CancellationToken ct)
    {
        var wait = "Bas mil gaya sab kuch! 🪁 Aapki personalized guide ban rahi hai — ek minute do mujhe.";
        await TrySendAsync(() => _messaging.SendTextAsync(student.Phone, wait, ct));
        _db.ChatMessages.Add(new ChatMessage
        {
            SessionId = session.Id, Role = MessageRole.Assistant, Content = wait
        });
        await _db.SaveChangesAsync(ct);

        try
        {
            var guide = await _engine.GenerateTwelfthGuideAsync(student, session, ct);
            var pdfUrl = await _pdf.GenerateGuideAsync(student, guide, ct);

            var summary = $"🎯 *Aapki 12th-ke-baad guide ready hai!*\n\n{guide.Greeting}\n\nPDF mein har option detailed hai — padh kar parents/teachers se discuss karo.";
            await TrySendAsync(() => _messaging.SendTextAsync(student.Phone, summary, ct));
            await TrySendAsync(() => _messaging.SendDocumentAsync(
                student.Phone, pdfUrl,
                "Your SkillKite 12th guide 🪁",
                $"SkillKite_12th_Guide_{student.Name ?? "student"}.pdf",
                ct));

            _db.ChatMessages.Add(new ChatMessage
            {
                SessionId = session.Id, Role = MessageRole.Assistant, Content = summary
            });
            await _db.SaveChangesAsync(ct);

            // PDF delivered → ask for feedback. Session moves to AwaitingFeedback.
            await SendFeedbackPromptAsync(student, session, ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "12th-flow guide generation failed for student {Id}", student.Id);
            session.Status = SessionStatus.Abandoned;
            await _db.SaveChangesAsync(ct);
            await TrySendAsync(() => _messaging.SendTextAsync(student.Phone,
                "Sorry yaar, guide generate karte time ek dikkat aa gayi. Thodi der baad try karenge. 🙏", ct));
        }
    }

    private static string NormaliseStream(string text)
    {
        var t = text.Trim().ToLowerInvariant();
        if (t is "pcm" or "pcb" or "commerce" or "arts" or "bba") return t;
        if (t.Contains("pcm") || (t.Contains("math") && !t.Contains("bio")))            return "pcm";
        if (t.Contains("pcb") || t.Contains("bio") || t.Contains("medical"))             return "pcb";
        if (t.Contains("commerce") || t.Contains("com"))                                 return "commerce";
        if (t.Contains("arts") || t.Contains("humanit"))                                 return "arts";
        if (t.Contains("bba") || t.Contains("voc"))                                      return "bba";
        return "not_sure";
    }

    private static string NormaliseDirection(string stream, string text)
    {
        var t = text.Trim().ToLowerInvariant();
        // Known ids from the lists above
        var knownIds = new[]
        {
            "engineering","pure_science","bca","architecture","defence",
            "medical","paramedical","pharmacy",
            "ca_cs_cma","bcom","bba","law","banking",
            "upsc","mass_comm","design","ba_hons",
            "mba","entrepreneurship","not_sure"
        };
        if (knownIds.Contains(t)) return t;

        if (t.Contains("sab") || t.Contains("show all") || t.Contains("not"))   return "not_sure";
        if (t.Contains("engineer") || t.Contains("btech") || t.Contains("b.tech")) return "engineering";
        if (t.Contains("doctor") || t.Contains("mbbs") || t.Contains("neet"))    return "medical";
        if (t.Contains("nursing") || t.Contains("paramed"))                     return "paramedical";
        if (t.Contains("pharm"))                                                 return "pharmacy";
        if (t.Contains("ca ") || t.Contains("chartered"))                       return "ca_cs_cma";
        if (t.Contains("upsc") || t.Contains("ssc") || t.Contains("govt"))      return "upsc";
        if (t.Contains("law") || t.Contains("clat"))                            return "law";
        if (t.Contains("mba"))                                                   return "mba";
        if (t.Contains("design") || t.Contains("nid") || t.Contains("nift"))    return "design";
        if (t.Contains("media") || t.Contains("journ"))                         return "mass_comm";
        if (t.Contains("bca") || t.Contains("computer"))                        return "bca";
        return "not_sure";
    }

    // ============================================================================
    // Skill upgrade flow — same thin-discovery pattern as 10th/12th, but for
    // working professionals (1-10 yrs experience). 3 steps:
    //   "name"  → free-text name
    //   "field" → 7-row list (current professional field)
    //   "goal"  → 5-row list (what they want next)
    // After "goal" we call Claude → render PDF → send. No degree-level
    // assessment, no week-by-week roadmap.
    // ============================================================================

    private async Task StartSkillUpgradeFlowAsync(Student student, CancellationToken ct)
    {
        var data = new Dictionary<string, string>
        {
            ["flowType"] = "upskill",
            ["step"]     = "name"
        };
        var session = new ChatSession
        {
            StudentId = student.Id,
            AssessmentDataJson = JsonSerializer.Serialize(data),
            Status = SessionStatus.Active
        };
        _db.ChatSessions.Add(session);
        await _db.SaveChangesAsync(ct);

        var ask = student.PreferredLanguage == PreferredLanguage.English
            ? "Great choice! 🌱 You're already working — let's plan the next rung.\n\nFirst — *what's your name?*"
            : "Sahi choice! 🌱 Aap already kaam kar rahe ho — chaliye next rung ke liye plan banate hain.\n\nPehle batao — *aapka naam kya hai?*";
        await TrySendAsync(() => _messaging.SendTextAsync(student.Phone, ask, ct));
        _db.ChatMessages.Add(new ChatMessage
        {
            SessionId = session.Id, Role = MessageRole.Assistant, Content = ask
        });
        await _db.SaveChangesAsync(ct);
    }

    private async Task HandleSkillUpgradeTurnAsync(Student student, ChatSession session, string text, CancellationToken ct)
    {
        _db.ChatMessages.Add(new ChatMessage
        {
            SessionId = session.Id, Role = MessageRole.User, Content = text
        });
        await _db.SaveChangesAsync(ct);

        var step = ReadField(session, "step") ?? "name";

        switch (step)
        {
            case "name":
            {
                var name = text.Trim();
                if (string.IsNullOrWhiteSpace(name) || name.Length > 60)
                    name = student.Name ?? "friend";
                student.Name = name;
                session.AssessmentDataJson = WriteField(session, ("name", name), ("step", "field"));
                await _db.SaveChangesAsync(ct);
                await SendUpskillFieldPromptAsync(student, session, ct);
                return;
            }
            case "field":
            {
                var field = NormaliseUpskillField(text);
                session.AssessmentDataJson = WriteField(session, ("field", field), ("step", "goal"));
                await _db.SaveChangesAsync(ct);
                await SendUpskillGoalPromptAsync(student, session, ct);
                return;
            }
            case "goal":
            {
                var goal = NormaliseUpskillGoal(text);
                session.AssessmentDataJson = WriteField(session, ("goal", goal), ("step", "generating"));
                await _db.SaveChangesAsync(ct);
                await DeliverSkillUpgradeGuideAsync(student, session, ct);
                return;
            }
            default:
                await TrySendAsync(() => _messaging.SendTextAsync(student.Phone,
                    "Aapki guide ban rahi hai 🪁 thoda ruko…", ct));
                return;
        }
    }

    private async Task SendUpskillFieldPromptAsync(Student student, ChatSession session, CancellationToken ct)
    {
        var name = student.Name ?? "friend";
        var body = $"Nice to meet you, {name}! 🙌\n\nAap abhi *kaunse field* mein kaam karte ho? Neeche se choose karo:";
        var options = new List<InteractiveOption>
        {
            new("software_it",      "💻 Software / IT",     "Dev, QA, DevOps, SRE"),
            new("data_analytics",   "📊 Data / Analytics",  "Analyst, DS, DE"),
            new("design_creative",  "🎨 Design / Creative", "UI/UX, graphic, video"),
            new("content_marketing","📝 Content / Marketing","Writer, SEO, social"),
            new("banking_finance",  "🏦 Banking / Finance", "Bank, fintech, accounting"),
            new("healthcare",       "🏥 Healthcare",         "Nurse, lab, pharma, hospital"),
            new("teaching_edu",     "🎓 Teaching / Edu",    "School, coaching, ed-tech"),
            new("ops_support",      "🛠️ Ops / Support",     "Customer support, ops, logistics"),
            new("other",            "🤷 Other / Mixed",     "Kuch aur — bot decide karega")
        };
        await TrySendAsync(() => _messaging.SendListAsync(
            student.Phone, body, "Choose one", "Aapka field", options, ct));

        _db.ChatMessages.Add(new ChatMessage
        {
            SessionId = session.Id, Role = MessageRole.Assistant, Content = body
        });
        await _db.SaveChangesAsync(ct);
    }

    private async Task SendUpskillGoalPromptAsync(Student student, ChatSession session, CancellationToken ct)
    {
        var body = "Got it 👍\n\n*Aage kya chahiye?* Neeche se choose karo:";
        var options = new List<InteractiveOption>
        {
            new("higher_salary_same", "💰 Same field, higher salary", "Promotion / better company"),
            new("switch_field",       "🔀 Switch to new field",       "Pivot — naya domain"),
            new("management",         "👔 Management track",          "Lead / EM / people mgr"),
            new("freelance",          "🚀 Freelance / own thing",     "Consulting / startup"),
            new("abroad",             "🌍 Remote / abroad",            "Global companies / visa route"),
            new("not_sure",           "🤷 Not sure — sab options",     "Show me everything")
        };
        await TrySendAsync(() => _messaging.SendListAsync(
            student.Phone, body, "Choose one", "Aapka next goal", options, ct));

        _db.ChatMessages.Add(new ChatMessage
        {
            SessionId = session.Id, Role = MessageRole.Assistant, Content = body
        });
        await _db.SaveChangesAsync(ct);
    }

    private async Task DeliverSkillUpgradeGuideAsync(Student student, ChatSession session, CancellationToken ct)
    {
        var wait = "Bas mil gaya sab kuch! 🪁 Aapki personalized upskill guide ban rahi hai — ek minute do mujhe.";
        await TrySendAsync(() => _messaging.SendTextAsync(student.Phone, wait, ct));
        _db.ChatMessages.Add(new ChatMessage
        {
            SessionId = session.Id, Role = MessageRole.Assistant, Content = wait
        });
        await _db.SaveChangesAsync(ct);

        try
        {
            var guide = await _engine.GenerateSkillUpgradeGuideAsync(student, session, ct);
            var pdfUrl = await _pdf.GenerateGuideAsync(student, guide, ct);

            var summary = $"🎯 *Aapki skill-upgrade guide ready hai!*\n\n{guide.Greeting}\n\nPDF mein skills, next roles, side moves — sab detailed hai. 3 mahine ke andar ek skill deeply seekho.";
            await TrySendAsync(() => _messaging.SendTextAsync(student.Phone, summary, ct));
            await TrySendAsync(() => _messaging.SendDocumentAsync(
                student.Phone, pdfUrl,
                "Your SkillKite upskill guide 🪁",
                $"SkillKite_Upskill_Guide_{student.Name ?? "professional"}.pdf",
                ct));

            _db.ChatMessages.Add(new ChatMessage
            {
                SessionId = session.Id, Role = MessageRole.Assistant, Content = summary
            });
            await _db.SaveChangesAsync(ct);

            // PDF delivered → ask for feedback. Session moves to AwaitingFeedback.
            await SendFeedbackPromptAsync(student, session, ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Upskill-flow guide generation failed for student {Id}", student.Id);
            session.Status = SessionStatus.Abandoned;
            await _db.SaveChangesAsync(ct);
            await TrySendAsync(() => _messaging.SendTextAsync(student.Phone,
                "Sorry yaar, guide generate karte time ek dikkat aa gayi. Thodi der baad try karenge. 🙏", ct));
        }
    }

    private static string NormaliseUpskillField(string text)
    {
        var t = text.Trim().ToLowerInvariant();
        var ids = new[] { "software_it","data_analytics","design_creative","content_marketing",
                          "banking_finance","healthcare","teaching_edu","ops_support","other" };
        if (ids.Contains(t)) return t;
        if (t.Contains("dev") || t.Contains("software") || t.Contains("coding") || t.Contains("engineer")) return "software_it";
        if (t.Contains("data") || t.Contains("analyt") || t.Contains("sql") || t.Contains("ml"))           return "data_analytics";
        if (t.Contains("design") || t.Contains("ui") || t.Contains("ux") || t.Contains("graphic"))         return "design_creative";
        if (t.Contains("content") || t.Contains("market") || t.Contains("seo") || t.Contains("social"))    return "content_marketing";
        if (t.Contains("bank") || t.Contains("finance") || t.Contains("account"))                          return "banking_finance";
        if (t.Contains("hospital") || t.Contains("nurse") || t.Contains("pharma") || t.Contains("health")) return "healthcare";
        if (t.Contains("teach") || t.Contains("school") || t.Contains("coach") || t.Contains("ed"))        return "teaching_edu";
        if (t.Contains("support") || t.Contains("ops") || t.Contains("logist") || t.Contains("customer")) return "ops_support";
        return "other";
    }

    private static string NormaliseUpskillGoal(string text)
    {
        var t = text.Trim().ToLowerInvariant();
        var ids = new[] { "higher_salary_same","switch_field","management","freelance","abroad","not_sure" };
        if (ids.Contains(t)) return t;
        if (t.Contains("salary") || t.Contains("paisa") || t.Contains("hike") || t.Contains("promotion")) return "higher_salary_same";
        if (t.Contains("switch") || t.Contains("pivot") || t.Contains("naya field"))                     return "switch_field";
        if (t.Contains("manag") || t.Contains("lead") || t.Contains("em "))                              return "management";
        if (t.Contains("freelance") || t.Contains("startup") || t.Contains("apna") || t.Contains("own")) return "freelance";
        if (t.Contains("abroad") || t.Contains("remote") || t.Contains("global") || t.Contains("visa"))  return "abroad";
        return "not_sure";
    }

    // ============================================================================
    // Small helpers for flow-typed sessions.
    // ============================================================================

    private static string? ReadFlowType(ChatSession session) => ReadField(session, "flowType");

    private static string? ReadField(ChatSession session, string key)
    {
        if (string.IsNullOrWhiteSpace(session.AssessmentDataJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(session.AssessmentDataJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            if (doc.RootElement.TryGetProperty(key, out var el) && el.ValueKind == JsonValueKind.String)
                return el.GetString();
        }
        catch { /* fall through */ }
        return null;
    }

    private static string WriteField(ChatSession session, params (string Key, string Value)[] updates)
    {
        var data = string.IsNullOrWhiteSpace(session.AssessmentDataJson) || session.AssessmentDataJson == "{}"
            ? new Dictionary<string, string>()
            : JsonSerializer.Deserialize<Dictionary<string, string>>(session.AssessmentDataJson)
              ?? new Dictionary<string, string>();
        foreach (var (k, v) in updates) data[k] = v;
        return JsonSerializer.Serialize(data);
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

        // Roadmap language preference. Legacy code path — kept so that any
        // session whose Claude-extracted data still carries roadmapLanguage
        // (e.g. an in-flight career session started before 2026-06-09's
        // upfront-language change) gets respected. Defaults to Hinglish when
        // the answer isn't a recognised English variant.
        if (data.TryGetValue("roadmapLanguage", out var lang) && !string.IsNullOrWhiteSpace(lang))
        {
            student.PreferredLanguage = lang.Trim().ToLowerInvariant() switch
            {
                "english" or "en" or "eng" => PreferredLanguage.English,
                _                          => PreferredLanguage.Hinglish,
            };
        }
    }

    // ============================================================================
    // User-initiated "reset / delete my data" flow (added 2026-06-09)
    //
    // Triggered by typing any of a handful of intent phrases in English,
    // Hinglish, or Hindi. Sends a 2-button confirmation; on Yes, deletes
    // ChatMessages, ChatSessions, Roadmaps, the Student row itself (via
    // FK cascade), and the PDFs on disk. On No or any other reply, just
    // abandons the confirm session and falls back to normal handling.
    //
    // DPDP Act 2023 compliance (India): students have the right to data
    // erasure; this is the in-band way to exercise it without emailing.
    // ============================================================================

    private static bool IsResetIntent(string text)
    {
        var t = text.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(t)) return false;

        // Skip button-id replies — they're underscore-prefixed and never contain
        // spaces. Stops "reset_yes" / "reset_no" from re-triggering the intent,
        // and stops "fb_*" / "lang_*" / "flow_*" / "return_*" from matching.
        if (!t.Contains(' '))
        {
            if (t.StartsWith("fb_") || t.StartsWith("lang_")
                || t.StartsWith("flow_") || t.StartsWith("return_")
                || t.StartsWith("reset_"))
                return false;
        }

        // Substring matches — covers "Mughe reset karna hai", "I want to reset
        // my data", "Bhai reset kar do please", etc. Plus the explicit phrases.
        if (t.Contains("reset"))         return true;
        if (t.Contains("delete my data")) return true;
        if (t.Contains("delete data"))   return true;
        if (t.Contains("data delete"))   return true;
        if (t.Contains("data hatao"))    return true;
        if (t.Contains("forget me"))     return true;
        if (t.Contains("wipe my data"))  return true;
        if (t.Contains("clear my data")) return true;

        // Devanagari variants
        if (text.Contains("रीसेट"))       return true;
        if (text.Contains("डेटा हटाओ"))   return true;
        if (text.Contains("डाटा डिलीट"))  return true;

        return false;
    }

    private async Task SendResetConfirmPromptAsync(Student student, CancellationToken ct)
    {
        var resetSession = new ChatSession
        {
            StudentId = student.Id,
            Status = SessionStatus.AwaitingResetConfirm
        };
        _db.ChatSessions.Add(resetSession);
        await _db.SaveChangesAsync(ct);

        var name = student.Name ?? "friend";
        var body = student.PreferredLanguage == PreferredLanguage.English
            ? $"Just to confirm, {name} — do you want me to delete ALL your SkillKite data?\n\nThat means: every chat message, every roadmap / guide PDF I made for you, and your profile. This cannot be undone. 🪁"
            : $"Confirm karo {name} — kya tumhara saara SkillKite data delete kar du?\n\nMatlab: saari chats, saare roadmap/guide PDFs, aur tumhari profile. Yeh undo nahi ho sakta. 🪁";

        var options = new List<InteractiveOption>
        {
            new("reset_yes", "✅ Haan, delete"),
            new("reset_no",  "❌ Nahi, keep")
        };

        await TrySendAsync(() => _messaging.SendButtonsAsync(student.Phone, body, options, ct));

        _db.ChatMessages.Add(new ChatMessage
        {
            SessionId = resetSession.Id,
            Role = MessageRole.Assistant,
            Content = body
        });
        await _db.SaveChangesAsync(ct);
    }

    private async Task HandleResetConfirmAsync(
        Student student, ChatSession resetSession, string text, CancellationToken ct)
    {
        // Persist the inbound reply BEFORE we potentially destroy everything.
        _db.ChatMessages.Add(new ChatMessage
        {
            SessionId = resetSession.Id,
            Role = MessageRole.User,
            Content = text
        });
        await _db.SaveChangesAsync(ct);

        var t = text.Trim().ToLowerInvariant();
        var confirmed = t is "reset_yes" or "yes" or "haan" or "हाँ" or "haan delete" or "haan, delete" or "✅";

        if (!confirmed)
        {
            // Either an explicit "no" tap or unrelated text. Either way, drop
            // the confirm session and let the next message flow through normal
            // dispatch (we DON'T forward `text` as the student's next intent —
            // they may have just been confused).
            resetSession.Status = SessionStatus.Abandoned;
            await _db.SaveChangesAsync(ct);

            var keep = student.PreferredLanguage == PreferredLanguage.English
                ? "OK, your SkillKite data is safe. 🪁 Carry on whenever you're ready."
                : "Theek hai — aapka data safe hai. 🪁 Jab time mile tab continue karna.";
            await TrySendAsync(() => _messaging.SendTextAsync(student.Phone, keep, ct));

            _db.ChatMessages.Add(new ChatMessage
            {
                SessionId = resetSession.Id,
                Role = MessageRole.Assistant,
                Content = keep
            });
            await _db.SaveChangesAsync(ct);
            return;
        }

        // Confirmed delete. Capture the few things we need before the wipe.
        var studentId = student.Id;
        var phone = student.Phone;
        var name = student.Name ?? "friend";
        var preferredLang = student.PreferredLanguage;

        // Send confirmation FIRST — once we delete the Student row, the FK cascade
        // takes ChatSessions and ChatMessages with it, so we can't persist a
        // reply after. The outbound text via WhatsApp doesn't need DB persistence.
        var done = preferredLang == PreferredLanguage.English
            ? $"Done {name}. Every chat, roadmap, and profile detail I had on you is gone. 🪁\n\nIf you ever want help again — just say Hi. We'll start completely fresh."
            : $"Done {name}. Saari chats, roadmaps aur profile sab delete ho gayi. 🪁\n\nKabhi bhi help chahiye ho — bas Hi bhejna, ekdum naya start karenge.";
        await TrySendAsync(() => _messaging.SendTextAsync(phone, done, ct));

        // FK cascade: deleting Student takes ChatSessions → ChatMessages and
        // Roadmaps with it (per AppDbContext config).
        _db.Students.Remove(student);
        await _db.SaveChangesAsync(ct);

        // Now the on-disk PDFs.
        try { await _pdf.DeletePdfsForStudentAsync(studentId, ct); }
        catch (Exception ex) { _log.LogWarning(ex, "PDF cleanup failed during reset for student {Id} (DB rows already deleted)", studentId); }

        _log.LogInformation("Student {Id} reset their data (phone hash {Hash})",
            studentId, phone.GetHashCode());
    }

    // ============================================================================
    // Post-delivery feedback prompt (added 2026-06-09)
    //
    // After ANY successful PDF delivery (career roadmap, 10th guide, 12th guide,
    // upskill guide), we send 3 reply buttons asking "kaisi lagi?". The session
    // sits in AwaitingFeedback until the student taps a button OR types something
    // else.
    //
    //   - Button tap → save rating, send ack, mark Completed.
    //   - Free text  → save "Skipped" rating, mark Completed, forward to the
    //                  existing post-roadmap chat handler so the student gets
    //                  a real reply.
    //
    // Rating + timestamp are stored in ChatSession.AssessmentDataJson under
    // "feedbackRating" / "feedbackAt" — no schema migration needed.
    // /api/stats reads from there to compute distributions.
    // ============================================================================

    /// <summary>
    /// Call this AFTER a PDF has been sent successfully to the student. Marks
    /// the session AwaitingFeedback immediately (so any free-text the student
    /// sends during the delay window routes to the feedback handler), then
    /// waits FeedbackPromptDelay so the PDF has time to actually reach the
    /// student's phone via Meta's relay, then sends the 3-button prompt.
    /// </summary>
    private async Task SendFeedbackPromptAsync(Student student, ChatSession session, CancellationToken ct)
    {
        session.Status = SessionStatus.AwaitingFeedback;
        await _db.SaveChangesAsync(ct);

        // WhatsApp Cloud API returns immediately on SendDocumentAsync but Meta's
        // relay takes 5–30 seconds to actually fetch + push the PDF to the
        // student's phone (longer on slower Tier 2/3 networks). Without this
        // delay, the feedback prompt appears before the PDF finishes downloading
        // — student ends up rating something they haven't read (Shivani, 06-09).
        try { await Task.Delay(FeedbackPromptDelay, ct); }
        catch (TaskCanceledException) { /* shutdown — still try to send the prompt below */ }

        var body = "🪁 PDF padh ke kaisi lagi batao — jab time mile tab ek tap kar dena.\n\nAapka feedback se agle student ke liye PDF aur improve karenge.";
        var options = new List<InteractiveOption>
        {
            new("fb_useful",    "👍 Useful"),
            new("fb_ok",        "😐 Theek hai"),
            new("fb_notuseful", "👎 Improve karo")
        };

        await TrySendAsync(() => _messaging.SendButtonsAsync(student.Phone, body, options, ct));

        _db.ChatMessages.Add(new ChatMessage
        {
            SessionId = session.Id,
            Role = MessageRole.Assistant,
            Content = body
        });
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// How long to wait between PDF delivery and the feedback button prompt.
    /// 25s covers Meta's typical PDF relay latency (5-30s) and gives the
    /// student a few seconds to start reading before being asked to rate.
    /// </summary>
    private static readonly TimeSpan FeedbackPromptDelay = TimeSpan.FromSeconds(25);

    /// <summary>
    /// Student replied while session was in AwaitingFeedback. Interpret as
    /// rating button tap or free text (= Skipped + forward to post-roadmap chat).
    /// </summary>
    private async Task HandleFeedbackAsync(Student student, ChatSession session, string text, CancellationToken ct)
    {
        _db.ChatMessages.Add(new ChatMessage
        {
            SessionId = session.Id,
            Role = MessageRole.User,
            Content = text
        });
        await _db.SaveChangesAsync(ct);

        var (rating, isButtonTap) = NormaliseFeedback(text);

        // Persist rating + timestamp in the session's assessment data blob.
        session.AssessmentDataJson = WriteField(session,
            ("feedbackRating", rating.ToString()),
            ("feedbackAt",     DateTime.UtcNow.ToString("o")));
        session.Status = SessionStatus.Completed;
        session.CompletedAt ??= DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        if (isButtonTap)
        {
            var name = student.Name ?? "friend";
            var ack = rating switch
            {
                FeedbackRating.Useful =>
                    $"Thanks {name}! 🪁 Apna roadmap save rakhna — kabhi bhi question ho toh bas message kar dena.",
                FeedbackRating.Ok =>
                    $"Thanks {name}! 😊 Specific kya improve karein? Ek line mein bata sakte ho — ya bas next se start karo, hum dhyan rakhenge.",
                FeedbackRating.NotUseful =>
                    $"Sorry yaar 🙏 — kya specifically miss laga? Ek line mein bata do toh main re-generate kar sakta hoon ya kuch aur try karenge.",
                _ => "Thanks!"
            };
            await TrySendAsync(() => _messaging.SendTextAsync(student.Phone, ack, ct));
            _db.ChatMessages.Add(new ChatMessage
            {
                SessionId = session.Id,
                Role = MessageRole.Assistant,
                Content = ack
            });
            await _db.SaveChangesAsync(ct);
        }
        else
        {
            // They typed real content instead of tapping. Forward to post-roadmap chat
            // so they get a meaningful reply (Q&A about their roadmap). Rating already
            // saved as "Skipped" above. We re-route the SAME inbound text through the
            // existing handler — no duplicate persistence (HandlePostRoadmapAsync also
            // appends a user message, but that's the SAME content for the same session
            // — duplicate by design, gives clean audit trail).
            await HandlePostRoadmapAsync(student, session, text, ct);
        }
    }

    private static (FeedbackRating rating, bool isButtonTap) NormaliseFeedback(string text)
    {
        var t = text.Trim().ToLowerInvariant();
        return t switch
        {
            "fb_useful"    => (FeedbackRating.Useful,    true),
            "fb_ok"        => (FeedbackRating.Ok,        true),
            "fb_notuseful" => (FeedbackRating.NotUseful, true),
            _              => (FeedbackRating.Skipped,   false)
        };
    }
}
