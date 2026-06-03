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

    public async Task<GeneratedRoadmap> GenerateRoadmapAsync(
        Student student,
        ChatSession session,
        CancellationToken ct = default)
    {
        var system = BuildRoadmapSystemPrompt(student.PreferredLanguage);
        var user = $$"""
            Student profile (extracted during assessment):
            {{session.AssessmentDataJson}}

            Known fields:
            - Name: {{student.Name ?? "unknown"}}
            - City: {{student.City ?? "unknown"}}
            - Education: {{student.EducationLevel ?? "unknown"}}
            - Preferred language: {{student.PreferredLanguage}}

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
        - Always save the [roadmapLanguage] question for LAST — asked only after every other anchor
          field is collected. Frame it as: roadmap is ready, just pick the language for the PDF.
        - When you have answers to ALL keys above (including roadmapLanguage), mark the assessment complete.
        - Never give career advice yet — just gather info warmly.

        INTERACTIVE QUESTIONS:
        Some anchor questions above are marked [INTERACTIVE buttons …] or [INTERACTIVE list …].
        When you ASK one of those questions, emit an "interactive" block in the JSON envelope so
        the student gets tappable options instead of typing. The student can still type freely if
        they prefer — typed answers are valid too.

        - For buttons: use the option ids exactly as listed; titles can be your own short labels.
        - For list: the salary salaryGoal question has fixed ranges — use those id+title pairs.
        - When the student's reply is an interactive option id (e.g. "phone", "full_time",
          "15-25k"), treat it as a clean, already-normalized answer and store it verbatim in
          "extracted". Do NOT translate or re-interpret.

        OUTPUT FORMAT — reply with a single JSON object, nothing else:
        {
          "reply": "<the message to send to the student>",
          "extracted": { "<question_key>": "<student's answer, normalized>", ... },
          "complete": <true|false>,
          "interactive": {                  // OPTIONAL — only when asking an INTERACTIVE question
            "type": "buttons" | "list",
            "body": "<short prompt text shown above the options; usually same as reply>",
            "options": [
              { "id": "phone",  "title": "📱 Sirf phone" },
              { "id": "laptop", "title": "💻 Laptop hai" },
              { "id": "both",   "title": "📱💻 Dono" }
            ],
            "buttonLabel": "Select",        // list only
            "sectionTitle": "Monthly goal", // list only
            "rowDescriptions": {            // list only — id → short description
              "10-15k": "Starting out, learning fast"
            }
          }
        }

        - "extracted" contains ONLY fields you newly learned this turn (can be empty {}).
        - Normalize: city names in Title Case. For free-text answers to closed questions,
          coerce into the listed option ids when possible:
            device      → "phone" | "laptop" | "both"
            workType    → "full_time" | "freelance" | "both"
            govtInterest → "yes" | "no" | "open"
            familyExpect → "job" | "study" | "both"
            dailyHours  → "1h" | "2-3h" | "fulltime"
            salaryGoal  → one of "10-15k" / "15-25k" / "25-40k" / "40-60k" / "60k+" or a free
                          number if student typed a specific custom amount
            roadmapLanguage → "hindi" | "english"
        - "complete": true only when every anchor key has been collected.

        Current question index hint: {{session.CurrentQuestionIndex}} of {{AssessmentQuestions.All.Count}}.
        Already extracted so far: {{session.AssessmentDataJson}}
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
        - If they say thanks / bye / "ok" / "got it" / a closing emoji → reply warmly,
          gently nudge them toward their Week 1 first task or the practice deliverable.
        - If they ask a follow-up question about the roadmap (e.g. "yeh course free hai?",
          "week 5 mein kya hoga?", "is salary realistic?") → answer directly using the
          roadmap data above.
        - If they seem confused or worried → reassure, name their first concrete step.
        - If they EXPLICITLY say they want a fresh roadmap / start over (e.g. "naya roadmap
          chahiye", "redo this", "start again") → ask ONE confirmation ("Sure? Tumhara
          existing roadmap save rahega"). Only if they confirm yes in their NEXT message,
          set shouldRestart=true.

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

    private static string ExtractJson(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "{}";
        var s = raw.Trim();
        // Strip ```json fences if Claude added them.
        if (s.StartsWith("```"))
        {
            var firstNl = s.IndexOf('\n');
            if (firstNl > 0) s = s[(firstNl + 1)..];
            if (s.EndsWith("```")) s = s[..^3];
            s = s.Trim();
        }
        var firstBrace = s.IndexOf('{');
        var lastBrace = s.LastIndexOf('}');
        if (firstBrace >= 0 && lastBrace > firstBrace)
            return s[firstBrace..(lastBrace + 1)];
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
