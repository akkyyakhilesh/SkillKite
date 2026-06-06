using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkillKite.Core.Dtos;
using SkillKite.Core.Enums;
using SkillKite.Core.Interfaces;
using SkillKite.Core.Models;
using SkillKite.Infrastructure.Configuration;

namespace SkillKite.Infrastructure.AI;

public class ClaudeCareerEngine : ICareerEngine
{
    private readonly HttpClient _http;
    private readonly ClaudeOptions _opts;
    private readonly ILogger<ClaudeCareerEngine> _log;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public ClaudeCareerEngine(HttpClient http, IOptions<ClaudeOptions> opts, ILogger<ClaudeCareerEngine> log)
    {
        _http = http;
        _opts = opts.Value;
        _log = log;

        _http.BaseAddress = new Uri(_opts.BaseUrl.TrimEnd('/') + "/");
        _http.DefaultRequestHeaders.Remove("x-api-key");
        _http.DefaultRequestHeaders.Add("x-api-key", _opts.ApiKey);
        _http.DefaultRequestHeaders.Remove("anthropic-version");
        _http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<AssessmentTurnResult> NextTurnAsync(
        ChatSession session,
        IReadOnlyList<ChatMessage> history,
        string? latestUserMessage,
        CancellationToken ct = default)
    {
        var system = BuildAssessmentSystemPrompt(session);
        var messages = history
            .Where(m => m.Role != MessageRole.System)
            .Select(m => new ClaudeMessage(m.Role == MessageRole.User ? "user" : "assistant", m.Content))
            .ToList();

        if (!string.IsNullOrWhiteSpace(latestUserMessage) &&
            (messages.Count == 0 || messages[^1].Role != "user"))
        {
            messages.Add(new ClaudeMessage("user", latestUserMessage));
        }

        if (messages.Count == 0)
        {
            // First-ever turn: prime the model to greet.
            messages.Add(new ClaudeMessage("user", "<<begin assessment>>"));
        }

        var raw = await CallClaudeAsync(system, messages, ct);
        return ParseAssessmentTurn(raw);
    }

    public async Task<CareerSuggestionsResult> SuggestCareerPathsAsync(
        Student student,
        ChatSession session,
        CancellationToken ct = default)
    {
        var system = BuildCareerSuggestionsSystemPrompt(student.PreferredLanguage);
        var user = $$"""
            Student profile (extracted during assessment):
            {{session.AssessmentDataJson}}

            Known fields:
            - Name: {{student.Name ?? "unknown"}}
            - City: {{student.City ?? "unknown"}}
            - Education: {{student.EducationLevel ?? "unknown"}}
            - Preferred language: {{student.PreferredLanguage}}

            Output the 3-career suggestions JSON now. Output ONLY the JSON object —
            no prose, no markdown fences.
            """;

        var raw = await CallClaudeAsync(system, new() { new("user", user) }, ct);
        var json = ExtractJson(raw);

        try
        {
            var parsed = JsonSerializer.Deserialize<CareerSuggestionsResult>(json, JsonOpts);
            if (parsed is null || parsed.Suggestions.Count == 0)
                throw new InvalidOperationException("Claude returned no career suggestions");
            return parsed;
        }
        catch (JsonException ex)
        {
            _log.LogError(ex, "Failed to parse career suggestions JSON. Raw: {Raw}", raw);
            throw;
        }
    }

    public async Task<GeneratedRoadmap> GenerateRoadmapAsync(
        Student student,
        ChatSession session,
        string? chosenCareerTitle = null,
        CancellationToken ct = default)
    {
        var system = BuildRoadmapSystemPrompt(student.PreferredLanguage);
        var chosenLine = string.IsNullOrWhiteSpace(chosenCareerTitle)
            ? "- Chosen career path: <not specified — pick the single best fit yourself>"
            : $"- Chosen career path: {chosenCareerTitle}  (the student picked this from the 3 suggestions — generate the roadmap for THIS career, not a different one)";

        var user = $$"""
            Student profile (extracted during assessment):
            {{session.AssessmentDataJson}}

            Known fields:
            - Name: {{student.Name ?? "unknown"}}
            - City: {{student.City ?? "unknown"}}
            - Education: {{student.EducationLevel ?? "unknown"}}
            - Preferred language: {{student.PreferredLanguage}}
            {{chosenLine}}

            Generate the roadmap JSON now. Output ONLY the JSON object — no prose, no markdown fences.
            """;

        var raw = await CallClaudeAsync(system, new() { new("user", user) }, ct);
        var json = ExtractJson(raw);

        try
        {
            var roadmap = JsonSerializer.Deserialize<GeneratedRoadmap>(json, JsonOpts);
            if (roadmap is null) throw new InvalidOperationException("Claude returned null roadmap");
            return roadmap;
        }
        catch (JsonException ex)
        {
            _log.LogError(ex, "Failed to parse roadmap JSON. Raw: {Raw}", raw);
            throw;
        }
    }

    public async Task<PostRoadmapTurnResult> PostRoadmapTurnAsync(
        Student student,
        GeneratedRoadmap roadmap,
        IReadOnlyList<ChatMessage> postRoadmapHistory,
        string latestUserMessage,
        CancellationToken ct = default)
    {
        var system = BuildPostRoadmapSystemPrompt(student, roadmap);

        var messages = postRoadmapHistory
            .Where(m => m.Role != MessageRole.System)
            .Select(m => new ClaudeMessage(m.Role == MessageRole.User ? "user" : "assistant", m.Content))
            .ToList();

        if (!string.IsNullOrWhiteSpace(latestUserMessage) &&
            (messages.Count == 0 || messages[^1].Role != "user"))
        {
            messages.Add(new ClaudeMessage("user", latestUserMessage));
        }

        if (messages.Count == 0)
            messages.Add(new ClaudeMessage("user", "<<post-roadmap turn>>"));

        var raw = await CallClaudeAsync(system, messages, ct);
        return ParsePostRoadmapTurn(raw);
    }

    // ----- prompts -----

    private static string BuildAssessmentSystemPrompt(ChatSession session)
    {
        // Build the anchor list. For closed-enum questions, append the allowed
        // option IDs so Claude knows which interactive block to emit.
        var qList = string.Join("\n", AssessmentQuestions.All.Select((q, i) =>
        {
            var optHint = q.Interactive == AssessmentQuestions.InteractiveKind.None
                ? ""
                : $"  [INTERACTIVE {q.Interactive.ToString().ToLowerInvariant()} — option ids: " +
                  string.Join(", ", q.Options!.Select(o => $"\"{o.Id}\"")) + "]";
            return $"  {i + 1}. [{q.Key}] EN: {q.English} | HI: {q.Hindi}{optHint}";
        }));

        return $$"""
        You are SkillKite, a warm, encouraging AI career coach for students in Tier 2/3 India.
        You speak Hinglish by default — natural mix of Hindi (Devanagari) and English, like how college
        students in Lucknow, Patna, or Indore actually text. Switch fully to English or Hindi if the
        student clearly prefers one.

        Your job RIGHT NOW: conduct a friendly career assessment, one question at a time.

        Anchor questions to cover (rephrase naturally, don't read like a form):
        {{qList}}

        Rules:
        - Ask ONE question per turn. Keep replies short — 1-3 sentences max.
        - Acknowledge what the student just said before asking the next question.
        - If the student volunteers info that answers a later question, skip that question.
        - If [govtInterest] = "yes" (they want a government job — SSC, banking, railways, etc.),
          implicitly set workType = "full_time" and SKIP the [workType] question entirely.
          Government jobs are structurally full-time, so asking about freelance/full-time after
          a govt answer is jarring.
        - Always save the [roadmapLanguage] question for LAST — asked only after every other anchor
          field is collected. Frame it as: roadmap is ready, just pick the language for the PDF.
        - When you have answers to ALL keys above (including roadmapLanguage), mark the assessment complete.
        - Never give career advice yet — just gather info warmly.

        INTERACTIVE QUESTIONS:
        Some anchor questions above are marked [INTERACTIVE buttons — option ids: …].
        When you ASK one of those questions, emit an "interactive" block in the JSON envelope so
        the student gets tappable buttons instead of typing. The student can still type freely if
        they prefer — typed answers are valid too.

        - Use the option ids EXACTLY as listed. Titles can be your own short emoji-led labels
          (≤ 20 chars per button, max 3 buttons per question).
        - When the student's reply is an interactive option id (e.g. "phone", "full_time",
          "25-50k"), treat it as a clean, already-normalized answer and store it verbatim in
          "extracted". Do NOT translate or re-interpret.

        OUTPUT FORMAT — reply with a SINGLE JSON object and absolutely nothing else.
        No prose, no markdown code fences (no ```json), no language identifier line, no
        "wait let me correct that" preamble or postamble, no second JSON block. If you
        realise mid-turn that your first draft was wrong, REPLACE it — do not append a
        correction. The orchestrator parses your reply directly; anything other than a
        single JSON object will be sent to the student verbatim and confuse them.
        The object schema is:
        {
          "reply": "<the message to send to the student>",
          "extracted": { "<question_key>": "<student's answer, normalized>", ... },
          "complete": <true|false>,
          "interactive": {                  // OPTIONAL — only when asking an INTERACTIVE question
            "type": "buttons",
            "body": "<short prompt text shown above the options; usually same as reply>",
            "options": [
              { "id": "phone",  "title": "📱 Sirf phone" },
              { "id": "laptop", "title": "💻 Laptop bhi hai" }
            ]
          }
        }

        - "extracted" contains ONLY fields you newly learned this turn (can be empty {}).
        - Normalize: city names in Title Case. For free-text answers to closed questions,
          coerce into the listed option ids when possible:
            device      → "phone" | "laptop"  (everyone has a phone; "laptop" means they
                                                also have a laptop. There is no "both".)
            workType    → "full_time" | "freelance" | "both"
            experience  → "real" (internship/freelance/job) | "college" (only academic projects)
                          | "none" (nothing yet)
            govtInterest → "yes" | "no" | "open"
            familyExpect → "job" | "study" | "both"
            dailyHours  → "1h" | "2-3h" | "4-5h"
            salaryGoal  → "10-25k" | "25-50k" | "50k+"  OR a free number if the student
                          typed a specific custom amount (e.g. "32000", "₹35k", "1 lakh").
            roadmapLanguage → "hindi" | "english"
        - "complete": true only when every anchor key has been collected.

        Current question index hint: {{session.CurrentQuestionIndex}} of {{AssessmentQuestions.All.Count}}.
        Already extracted so far: {{session.AssessmentDataJson}}
        """;
    }

    private static string BuildCareerSuggestionsSystemPrompt(PreferredLanguage lang)
    {
        var languageDirective = lang == PreferredLanguage.English
            ? "Write the introLine, titles, and rationales in English."
            : "Write the introLine and rationales in natural Hinglish (English words for tech terms are fine). Career titles stay in their canonical form (e.g. 'Junior Web Developer', 'Content Writer') — those are recognised job descriptions and should not be translated.";

        return $$"""
        You are SkillKite. The student has just completed their assessment.
        Your ONLY job in this turn: suggest the THREE best-fit career paths for
        this specific student. The student will pick one — only then will a full
        20-week roadmap be generated.

        Hard rules:
        - Output exactly 3 suggestions. Not 2, not 4.
        - Each title is short, recognisable, 3-4 words MAX. Examples:
          "Junior Web Developer", "Content Writer", "Mobile App Developer",
          "Cloud Engineer", "Freelance Graphic Designer", "Digital Marketing Executive",
          "Data Analyst", "Backend Developer", "Bank PO (SSC track)".
        - Title length cap: 20 characters TOTAL (so they fit on a WhatsApp button).
          If a natural title is longer, shorten it sensibly without losing meaning.
        - Each id is a short lowercase slug version of the title with underscores
          (e.g. "junior_web_dev", "content_writer", "freelance_designer").
        - Each rationale is ONE line, ≤ 100 chars, naming the specific assessment
          field that drove this pick (skills / education / device / salaryGoal / etc).
          Examples:
            "BCA + coding interest + 2-3h daily — strong fit for first dev role"
            "Phone-only + writing skill — earn from anywhere with no laptop needed"
        - Pick a DIVERSE set. The 3 careers should not all be variants of the same
          path. If 2 of them are similar (e.g. "Web Developer" and "Frontend Developer"),
          replace one with a meaningfully different option.
        - If the student profile contains "previousCareer" — that's a career they
          ALREADY got a roadmap for and have come back asking for fresh options.
          DO NOT suggest that same career again. The 3 new picks must be meaningfully
          different from the previousCareer value. Prioritise neighbouring careers
          that share their skill base but go in a different direction.
        - Honour hard constraints from the assessment:
            device=phone     → at least one of the 3 must be runnable from a phone
            workType=freelance → at least one of the 3 must be freelance-friendly
            govtInterest=yes → include a govt-track option (SSC / Bank PO / Railway)
            education=10th/12th → include a stream- or course-decision-style suggestion
                                  (e.g. "12th + JEE prep" rather than a degree-only path)
        - {{languageDirective}}

        OUTPUT FORMAT — reply with a single JSON object, nothing else:
        {
          "introLine": "<one short, warm line introducing the 3 options to the student>",
          "suggestions": [
            { "id": "<slug>", "title": "<3-4 words>", "rationale": "<one line, ≤100 chars>" },
            { "id": "<slug>", "title": "<3-4 words>", "rationale": "<one line, ≤100 chars>" },
            { "id": "<slug>", "title": "<3-4 words>", "rationale": "<one line, ≤100 chars>" }
          ]
        }
        """;
    }

    private static string BuildRoadmapSystemPrompt(PreferredLanguage lang)
    {
        // We always emit both EN and HI fields in the JSON schema (storage + future-proofing
        // for a PWA dashboard that could let the user flip languages), but only one set is
        // *written naturally*. The other set is left as an empty string — the PDF generator
        // renders only the preferred language so the student gets a clean monolingual document.
        var (primary, secondary) = lang == PreferredLanguage.English
            ? ("English", "Hindi")
            : ("Hindi (Devanagari script; English loanwords are fine for tech terms like 'developer', 'internship', 'YouTube')",
               "English");

        var (primaryFields, secondaryFields) = lang == PreferredLanguage.English
            ? ("careerTitle, summary, theme",  "careerTitleHi, summaryHi, themeHi — leave as \"\"")
            : ("careerTitleHi, summaryHi, themeHi", "careerTitle, summary, theme — leave as \"\"");

        return $$"""
        You are SkillKite's roadmap generator. Given a student's assessment data, output a
        personalized, realistic career roadmap as STRICT JSON matching this schema:

        {
          "careerTitle": "string",
          "careerTitleHi": "string",
          "summary": "string",
          "summaryHi": "string",
          "totalWeeks": <int, 12-24>,
          "expectedSalaryMin": <int, INR/month entry-level>,
          "expectedSalaryMax": <int, INR/month after 1 year>,
          "weeks": [
            {
              "weekNumber": 1,
              "theme": "string",
              "themeHi": "string",
              "goals": ["string", "string"],
              "resources": [{ "title": "string", "url": "string", "platform": "youtube|nptel|coursera|skillIndia|other" }],
              "practice": "string — one concrete deliverable for the week"
            }
          ]
        }

        LANGUAGE RULES (critical):
        - The student chose {{primary}} as their preferred language.
        - Write {{primaryFields}}, and EVERY string in "goals" and "practice", in {{primary}} only.
        - For {{secondaryFields}} — leave them as empty strings ("").
        - "resources[].title" stays in the resource's native language (most YouTube/NPTEL titles are
          English or already-Hindi; do NOT translate them).
        - "resources[].url" and "platform" are always plain ASCII.

        Hard rules:
        - Pick the SINGLE best-fit career given device, location, family constraints, daily hours.
        - Free resources only (YouTube, NPTEL, Skill India Digital, Coursera free audit). Real URLs only — no placeholders.
        - Phone-only? Pick a phone-friendly career (content, freelance writing, Canva design, reselling).
        - Hindi-first? Prioritize Hindi-language resources.
        - Salary ranges must be realistic for Tier 2/3 India entry-level.
        - Output JSON ONLY. No markdown, no commentary.
        """;
    }

    private static string BuildPostRoadmapSystemPrompt(Student student, GeneratedRoadmap roadmap)
    {
        var hi = student.PreferredLanguage == PreferredLanguage.Hindi;
        var careerTitle = hi && !string.IsNullOrWhiteSpace(roadmap.CareerTitleHi)
            ? roadmap.CareerTitleHi : roadmap.CareerTitle;
        var summary = hi && !string.IsNullOrWhiteSpace(roadmap.SummaryHi)
            ? roadmap.SummaryHi : roadmap.Summary;
        var week1 = roadmap.Weeks?.FirstOrDefault();
        var week1Theme = week1 != null
            ? (hi && !string.IsNullOrWhiteSpace(week1.ThemeHi) ? week1.ThemeHi : week1.Theme)
            : "Week 1";
        var week1Goals = week1?.Goals != null ? string.Join(" • ", week1.Goals) : "";
        var week1Practice = week1?.Practice ?? "";

        return $$"""
        You are SkillKite, continuing to chat with {{student.Name ?? "the student"}}
        AFTER their roadmap PDF has already been delivered. DO NOT restart the
        assessment under any circumstances unless the student EXPLICITLY confirms
        they want a brand-new one (and you've asked them once to be sure).

        The student's context:
        - Name: {{student.Name ?? "unknown"}}
        - City: {{student.City ?? "unknown"}}
        - Education: {{student.EducationLevel ?? "unknown"}}
        - Preferred language: {{student.PreferredLanguage}}

        Their generated career roadmap:
        - Career path: {{careerTitle}}
        - Duration: {{roadmap.TotalWeeks}} weeks
        - Expected salary: ₹{{roadmap.ExpectedSalaryMin:N0}}–₹{{roadmap.ExpectedSalaryMax:N0}}/month
        - Summary: {{summary}}
        - Week 1 theme: {{week1Theme}}
        - Week 1 goals: {{week1Goals}}
        - Week 1 practice deliverable: {{week1Practice}}

        Conversation rules:
        - Keep the same warm Hinglish voice you had during the assessment.
        - Replies are SHORT — 1-3 sentences max.

        - FIRST POST-ROADMAP MESSAGE (the very first thing they say after the PDF
          arrived — usually "Hi", "thanks", "got it", a 👍 emoji, or similar). In
          this opening turn, your reply should warmly acknowledge them AND make
          the three available paths explicit so they know what's possible. Example
          structure:
            "Welcome back {name}! 🪁 How can I help — koi question hai roadmap pe,
             naya roadmap chahiye, ya bas Week 1 ka pehla step start karein?"
          Use natural Hinglish, but make sure all THREE paths (question / new
          roadmap / start week 1) are visible. Do this only on the FIRST post-
          roadmap turn — subsequent turns are free conversation.

        - SUBSEQUENT turns:
            * If they ask a follow-up question (e.g. "yeh course free hai?",
              "week 5 mein kya hoga?", "is salary realistic?") → answer directly
              using the roadmap data above.
            * If they seem confused or worried → reassure, name their first concrete step.
            * If they say thanks/bye/closer → warm closer + nudge Week 1 first task.

        - RESTART REQUEST handling:
            * If they EXPLICITLY say they want a fresh roadmap / start over
              ("naya roadmap chahiye", "redo this", "start again"), ask ONE
              confirmation: "Sure? Tumhara existing roadmap save rahega — naya
              banaya toh purana replace ho jayega. Confirm karna chahti ho?"
              Only set shouldRestart=true once they confirm in their NEXT message.
            * When restart is confirmed: the orchestrator will reuse all the
              student's previously-given answers (name, education, city, skills,
              salary goal, etc.). They will NOT re-do the 13-question assessment.
              They will go straight to 3 fresh career options. So when you confirm
              the restart, set shouldRestart=true and reply with something brief
              like "Got it! 3 fresh career options nikal raha hoon — ek minute…"
              Do NOT say "let's start a new assessment" — there is no re-assessment.

        OUTPUT FORMAT — reply with a single JSON object, nothing else:
        {
          "reply": "<message to send to the student>",
          "shouldRestart": <true|false>
        }

        Set shouldRestart=true ONLY when the student has just confirmed they want a brand
        new assessment. Default is false.
        """;
    }

    // ----- HTTP -----

    private async Task<string> CallClaudeAsync(string system, List<ClaudeMessage> messages, CancellationToken ct)
    {
        var req = new ClaudeRequest(
            Model: _opts.Model,
            MaxTokens: _opts.MaxTokens,
            System: system,
            Messages: messages);

        using var resp = await _http.PostAsJsonAsync("messages", req, JsonOpts, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            _log.LogError("Claude API error {Status}: {Body}", resp.StatusCode, body);
            resp.EnsureSuccessStatusCode();
        }

        var parsed = JsonSerializer.Deserialize<ClaudeResponse>(body, JsonOpts);
        var text = parsed?.Content?.FirstOrDefault(c => c.Type == "text")?.Text ?? "";
        return text;
    }

    // ----- parsing -----

    private static AssessmentTurnResult ParseAssessmentTurn(string raw)
    {
        var json = ExtractJson(raw);

        JsonDocument? doc = null;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException)
        {
            // Claude occasionally replies in plain conversational text instead of the
            // structured envelope. Treat the whole thing as the reply, no extraction.
            return new AssessmentTurnResult(raw.Trim(), IsComplete: false, ExtractedFields: null);
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return new AssessmentTurnResult(raw.Trim(), false, null);

            var reply = root.TryGetProperty("reply", out var r) && r.ValueKind == JsonValueKind.String
                ? r.GetString() ?? raw
                : raw;
            var complete = root.TryGetProperty("complete", out var c) && c.ValueKind == JsonValueKind.True;

            Dictionary<string, string>? extracted = null;
            if (root.TryGetProperty("extracted", out var ex) && ex.ValueKind == JsonValueKind.Object)
            {
                extracted = new Dictionary<string, string>();
                foreach (var prop in ex.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.String)
                        extracted[prop.Name] = prop.Value.GetString() ?? "";
                    else
                        extracted[prop.Name] = prop.Value.GetRawText();
                }
            }

            var interactive = TryParseInteractiveBlock(root);

            return new AssessmentTurnResult(reply, complete, extracted, interactive);
        }
    }

    private static InteractiveBlock? TryParseInteractiveBlock(JsonElement root)
    {
        if (!root.TryGetProperty("interactive", out var ix) || ix.ValueKind != JsonValueKind.Object)
            return null;

        var type = ix.TryGetProperty("type", out var t) ? t.GetString() : null;
        if (type != "buttons" && type != "list") return null;

        var body = ix.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";

        if (!ix.TryGetProperty("options", out var opts) || opts.ValueKind != JsonValueKind.Array)
            return null;

        // For list rows we accept descriptions either embedded inside each option
        // (preferred) or as a separate "rowDescriptions" id→description map.
        Dictionary<string, string>? descMap = null;
        if (type == "list" && ix.TryGetProperty("rowDescriptions", out var rd) &&
            rd.ValueKind == JsonValueKind.Object)
        {
            descMap = new Dictionary<string, string>();
            foreach (var prop in rd.EnumerateObject())
                if (prop.Value.ValueKind == JsonValueKind.String)
                    descMap[prop.Name] = prop.Value.GetString() ?? "";
        }

        var options = new List<InteractiveOption>();
        foreach (var opt in opts.EnumerateArray())
        {
            if (opt.ValueKind != JsonValueKind.Object) continue;
            var id    = opt.TryGetProperty("id",    out var oi) ? oi.GetString() : null;
            var title = opt.TryGetProperty("title", out var ot) ? ot.GetString() : null;
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(title)) continue;

            var description = opt.TryGetProperty("description", out var od)
                ? od.GetString()
                : (descMap != null && descMap.TryGetValue(id, out var dd) ? dd : null);

            options.Add(new InteractiveOption(id, title, description));
        }
        if (options.Count == 0) return null;

        var buttonLabel  = ix.TryGetProperty("buttonLabel",  out var bl) ? bl.GetString() : null;
        var sectionTitle = ix.TryGetProperty("sectionTitle", out var st) ? st.GetString() : null;

        return new InteractiveBlock(type, body, options, buttonLabel, sectionTitle);
    }

    private static PostRoadmapTurnResult ParsePostRoadmapTurn(string raw)
    {
        var json = ExtractJson(raw);

        JsonDocument? doc = null;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException)
        {
            // Fallback to treating the whole response as a friendly reply.
            return new PostRoadmapTurnResult(raw.Trim(), ShouldRestart: false);
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return new PostRoadmapTurnResult(raw.Trim(), false);

            var reply = root.TryGetProperty("reply", out var r) && r.ValueKind == JsonValueKind.String
                ? r.GetString() ?? raw
                : raw;
            var shouldRestart = root.TryGetProperty("shouldRestart", out var sr) && sr.ValueKind == JsonValueKind.True;

            return new PostRoadmapTurnResult(reply, shouldRestart);
        }
    }

    /// <summary>
    /// Extract a parseable JSON object from Claude's raw response.
    ///
    /// Claude sometimes wraps responses in markdown ```json fences, emits a
    /// language hint ("json") on its own line, or — worst case — produces a
    /// JSON block followed by prose like "wait, let me correct that" followed
    /// by a second JSON block. We need to be robust to all three.
    ///
    /// Strategy: walk the text scanning for brace-balanced candidate objects,
    /// try to JSON-parse each one, return the LAST one that parses cleanly
    /// (Claude's most recent "intended" response). Falls back to the legacy
    /// first-brace / last-brace span if none parse, and finally the raw text.
    /// </summary>
    private static string ExtractJson(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "{}";

        // Strip code fences anywhere they appear (could be multiple).
        var s = raw.Replace("```json", "```", StringComparison.OrdinalIgnoreCase)
                   .Replace("```", "");

        // Collect all top-level brace-balanced spans.
        var candidates = new List<string>();
        int depth = 0, start = -1;
        bool inString = false;
        for (int i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (inString)
            {
                if (c == '\\' && i + 1 < s.Length) { i++; continue; }
                if (c == '"') inString = false;
                continue;
            }
            switch (c)
            {
                case '"': inString = true; break;
                case '{':
                    if (depth == 0) start = i;
                    depth++;
                    break;
                case '}':
                    depth--;
                    if (depth == 0 && start >= 0)
                    {
                        candidates.Add(s[start..(i + 1)]);
                        start = -1;
                    }
                    break;
            }
        }

        // Take the LAST candidate that parses cleanly — Claude often emits a
        // first attempt then "corrects" itself with a second block.
        for (int i = candidates.Count - 1; i >= 0; i--)
        {
            try { using var _ = JsonDocument.Parse(candidates[i]); return candidates[i]; }
            catch (JsonException) { /* try the next candidate up the list */ }
        }

        // Last resort: legacy span between first { and last } (may include
        // prose between two JSON blocks — caller's try/parse will fail safely).
        var first = s.IndexOf('{');
        var last = s.LastIndexOf('}');
        if (first >= 0 && last > first) return s[first..(last + 1)];
        return s;
    }

    // ----- Claude API DTOs -----

    private record ClaudeMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private record ClaudeRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        [property: JsonPropertyName("system")] string System,
        [property: JsonPropertyName("messages")] List<ClaudeMessage> Messages);

    private record ClaudeResponse(
        [property: JsonPropertyName("content")] List<ClaudeContent>? Content);

    private record ClaudeContent(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("text")] string? Text);
}
