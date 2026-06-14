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
        Student student,
        ChatSession session,
        IReadOnlyList<ChatMessage> history,
        string? latestUserMessage,
        CancellationToken ct = default)
    {
        var system = BuildAssessmentSystemPrompt(student, session);
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

    private static string BuildAssessmentSystemPrompt(Student student, ChatSession session)
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

        var languageDirective = student.PreferredLanguage == PreferredLanguage.English
            ? "REPLY LANGUAGE: Pure professional English throughout. Do not mix Hindi words in. The student picked English upfront because they prefer a fully English experience."
            : "REPLY LANGUAGE: Hinglish — natural mix of Hindi (Latin script) and English, like how college students in Lucknow, Patna, or Indore actually text. Use Hindi words for warmth ('haan', 'bhai', 'kya'), English for tech and structural terms. Do NOT use Devanagari script — student prefers Latin-script Hinglish.";

        return $$"""
        You are SkillKite, a warm, encouraging AI career coach for students in Tier 2/3 India.

        {{languageDirective}}

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
        - When you have answers to ALL keys above, mark the assessment complete.
        - Never give career advice yet — just gather info warmly.

        NOTE: We removed the old "what language for the PDF?" question — student picked
        language upfront, before this assessment started. Do NOT ask language again here.

        RETURNING STUDENTS (already-extracted name):
        - If "Already extracted so far" already contains a "name" value, the student is NOT new —
          we have already greeted them in a separate menu turn. Do NOT re-introduce yourself
          ("Heyy! 👋 Welcome! Main hoon SkillKite…") and do NOT ask for the name again. Just
          warmly acknowledge them by name and continue with the next unanswered anchor question.
        - Same applies to "city" and "education": if already extracted, do NOT ask again — pick
          up at the next missing field.

        EXTRACTION RELIABILITY (do not lose answers):
        - If the student's reply is a single-token recognized option id for the question you just
          asked (e.g. "english", "hindi", "phone", "laptop", "full_time", "25-50k", "yes", "no",
          "real", "college", "none", "1h", "2-3h", "4-5h"), you MUST include that field in
          "extracted" this turn. Never re-ask a question whose answer was already given as a
          recognized option id — that breaks the student's trust.
        - PARAGRAPH ANSWERS: working professionals and self-aware students often answer multiple
          anchor questions in a single sentence — e.g. "Currently I work as a Storage Engineer at
          CGI, pursuing MCA, last semester exams this month, based in Bangalore." That ONE message
          gives you education (MCA / final year), city (Bangalore), AND a strong skill/role signal
          (working in storage / infrastructure → "skills"). Extract EVERY anchor field present in
          the sentence in a single turn — using ONLY keys from the anchor list above (e.g.
          "name", "education", "city", "skills", "workType") — and store them in "extracted".
          Then ask the NEXT unanswered anchor, not anything they already
          told you. Re-asking what was clearly in their paragraph is the #1 way to make a real
          user say "you are asking the same questions as earlier" (caught from Shivani 06-10).
        - When you re-ask anyway because the previous turn looks ambiguous, FIRST acknowledge
          you may have missed it ("Maine miss kar diya kya — confirm karo:" / English: "I may have
          missed this — confirm:") so the student knows why.

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

        YOUTUBE URL RULES (critical — broken links destroy student trust):
        - NEVER output youtube.com/watch?v=... or youtu.be/... URLs. You cannot know live video
          IDs; invented ones lead students to deleted/wrong videos.
        - YouTube resource URLs must be CHANNEL-level (https://www.youtube.com/@handle) only.
        - Keep "title" specific ("Class 10 Physics Full Course") — the student searches the
          channel for it. The URL just lands them on the right channel.
        - Preferred channels (use when the subject fits; otherwise name any well-known channel
          you are CERTAIN exists, channel-level URL only):
          * @freecodecamp — programming, web dev, data (English)
          * @PhysicsWallah — 10th/12th science, NEET/JEE (Hindi)
          * @CodeWithHarry — programming, web dev (Hindi)
          * @ApnaCollegeOfficial — DSA, placement prep (Hindi)
          * @khanacademy — maths, science fundamentals (English)
          * @nptelhrd — engineering, university-level courses (English)
          * @studyiq — UPSC, SSC, banking, govt exams (Hindi)
          * @Adda247 — banking, SSC exam prep (Hindi)
          * @Telusko — Java, Python (English)
          * @TechnicalGuruji — tech awareness, gadgets (Hindi)
        """;
    }

    private static string BuildPostRoadmapSystemPrompt(Student student, GeneratedRoadmap roadmap)
    {
        var english = student.PreferredLanguage == PreferredLanguage.English;
        var languageDirective = english
            ? "REPLY LANGUAGE: Pure professional English throughout. Do not mix Hindi words in. The student picked English upfront because they prefer a fully English experience — keep that promise even AFTER the PDF is delivered."
            : "REPLY LANGUAGE: Hinglish — natural mix of Hindi (Latin script) and English, like how college students in Lucknow, Patna, or Indore actually text. Use Hindi words for warmth ('haan', 'bhai', 'kya'), English for tech and structural terms. Do NOT use Devanagari script — student prefers Latin-script Hinglish.";
        var openingExample = english
            ? "\"Welcome back {name}! 🪁 How can I help — got a question on your roadmap, want a fresh roadmap, or shall we kick off Week 1's first step?\""
            : "\"Welcome back {name}! 🪁 How can I help — koi question hai roadmap pe, naya roadmap chahiye, ya bas Week 1 ka pehla step start karein?\"";
        var restartConfirmExample = english
            ? "\"Sure? Your existing roadmap will stay saved — if you start fresh, the old one gets replaced. Confirm?\""
            : "\"Sure? Tumhara existing roadmap save rahega — naya banaya toh purana replace ho jayega. Confirm karna chahti ho?\"";
        var restartAckExample = english
            ? "\"Got it! Pulling 3 fresh career options — give me a minute…\""
            : "\"Got it! 3 fresh career options nikal raha hoon — ek minute…\"";
        // No more pure Hindi rendering. Hinglish (default) and English both use
        // the primary fields — Claude's prompt already produces content in the
        // student's chosen mode, so the *Hi fields are redundant. Legacy data
        // (sessions completed before 2026-06-09) may still have the Hi fields
        // populated but we just don't read them anymore.
        var careerTitle = roadmap.CareerTitle;
        var summary = roadmap.Summary;
        var week1 = roadmap.Weeks?.FirstOrDefault();
        var week1Theme = week1?.Theme ?? "Week 1";
        var week1Goals = week1?.Goals != null ? string.Join(" • ", week1.Goals) : "";
        var week1Practice = week1?.Practice ?? "";

        return $$"""
        You are SkillKite, continuing to chat with {{student.Name ?? "the student"}}
        AFTER their roadmap PDF has already been delivered. DO NOT restart the
        assessment under any circumstances unless the student EXPLICITLY confirms
        they want a brand-new one (and you've asked them once to be sure).

        {{languageDirective}}

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
        - Keep the same warm voice you had during the assessment, in the language above.
        - Replies are SHORT — 1-3 sentences max.

        - FIRST POST-ROADMAP MESSAGE (the very first thing they say after the PDF
          arrived — usually "Hi", "thanks", "got it", a 👍 emoji, or similar). In
          this opening turn, your reply should warmly acknowledge them AND make
          the three available paths explicit so they know what's possible. Example
          structure:
            {{openingExample}}
          Match the example's tone, but make sure all THREE paths (question /
          new roadmap / start week 1) are visible. Do this only on the FIRST
          post-roadmap turn — subsequent turns are free conversation.

        - SUBSEQUENT turns:
            * If they ask a follow-up question (e.g. "yeh course free hai?",
              "week 5 mein kya hoga?", "is salary realistic?") → answer directly
              using the roadmap data above.
            * If they seem confused or worried → reassure, name their first concrete step.
            * If they say thanks/bye/closer → warm closer + nudge Week 1 first task.

        - RESTART REQUEST handling:
            * If they EXPLICITLY say they want a fresh roadmap / start over
              ("naya roadmap chahiye", "redo this", "start again"), ask ONE
              confirmation in the student's language, e.g.:
                {{restartConfirmExample}}
              Only set shouldRestart=true once they confirm in their NEXT message.
            * When restart is confirmed: the orchestrator will reuse all the
              student's previously-given answers (name, education, city, skills,
              salary goal, etc.). They will NOT re-do the 13-question assessment.
              They will go straight to 3 fresh career options. So when you confirm
              the restart, set shouldRestart=true and reply briefly, e.g.:
                {{restartAckExample}}
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
    /// <summary>
    /// Returns a 2-line OUTPUT LANGUAGE block to prepend to a guide / suggestion
    /// system prompt so Claude knows whether to produce Hinglish (default) or
    /// pure English content. Picked upfront by the student before the first
    /// real flow question (see SendLanguageChoicePromptAsync in the orchestrator).
    /// </summary>
    private static string LanguageDirective(PreferredLanguage lang) =>
        lang == PreferredLanguage.English
            ? "OUTPUT LANGUAGE: Pure professional English throughout. Do not mix Hindi words in. The student picked English upfront because they prefer a fully English experience.\n\n"
            : "OUTPUT LANGUAGE: Hinglish — natural mix of Hindi (Latin script) and English. Use Hindi words for warmth ('haan', 'bhai', 'kya', 'bahut achha'), English for tech and structural terms. Do NOT use Devanagari script — student prefers Latin-script Hinglish.\n\n";

    public async Task<StudentGuide> GenerateTenthGuideAsync(
        Student student, ChatSession session, CancellationToken ct = default)
    {
        var system = LanguageDirective(student.PreferredLanguage) + BuildTenthGuideSystemPrompt();
        var user = $$"""
            Student profile (from the 2-question 10th flow):
            {{session.AssessmentDataJson}}

            Known fields:
            - Name: {{student.Name ?? "unknown"}}
            - Preferred language: {{student.PreferredLanguage}}

            Generate the StudentGuide JSON now. Output ONLY the JSON object —
            no prose, no markdown fences. flowLabel MUST be "10th".
            """;

        var raw = await CallClaudeAsync(system, new() { new("user", user) }, ct);
        return ParseGuide(raw, fallbackFlowLabel: "10th");
    }

    public async Task<StudentGuide> GenerateTwelfthGuideAsync(
        Student student, ChatSession session, CancellationToken ct = default)
    {
        var system = LanguageDirective(student.PreferredLanguage) + BuildTwelfthGuideSystemPrompt();
        var user = $$"""
            Student profile (from the 3-question 12th flow):
            {{session.AssessmentDataJson}}

            Known fields:
            - Name: {{student.Name ?? "unknown"}}
            - Preferred language: {{student.PreferredLanguage}}

            Generate the StudentGuide JSON now. Output ONLY the JSON object —
            no prose, no markdown fences. flowLabel MUST be "12th".
            """;

        var raw = await CallClaudeAsync(system, new() { new("user", user) }, ct);
        return ParseGuide(raw, fallbackFlowLabel: "12th");
    }

    public async Task<StudentGuide> GenerateSkillUpgradeGuideAsync(
        Student student, ChatSession session, CancellationToken ct = default)
    {
        var system = LanguageDirective(student.PreferredLanguage) + BuildSkillUpgradeSystemPrompt();
        var user = $$"""
            Working professional's profile (from the 3-question upskill flow):
            {{session.AssessmentDataJson}}

            Known fields:
            - Name: {{student.Name ?? "unknown"}}
            - Preferred language: {{student.PreferredLanguage}}

            Generate the StudentGuide JSON now. Output ONLY the JSON object —
            no prose, no markdown fences. flowLabel MUST be "Upskill".
            """;

        var raw = await CallClaudeAsync(system, new() { new("user", user) }, ct);
        return ParseGuide(raw, fallbackFlowLabel: "Upskill");
    }

    private static StudentGuide ParseGuide(string raw, string fallbackFlowLabel)
    {
        var json = ExtractJson(raw);
        var parsed = JsonSerializer.Deserialize<StudentGuide>(json, JsonOpts)
            ?? throw new InvalidOperationException("Claude returned empty guide JSON");
        if (string.IsNullOrWhiteSpace(parsed.FlowLabel))
            parsed = parsed with { FlowLabel = fallbackFlowLabel };
        if (parsed.Sections is null || parsed.Sections.Count == 0)
            throw new InvalidOperationException("Claude returned no guide sections");
        return parsed;
    }

    private static string BuildTenthGuideSystemPrompt() => """
        You are SkillKite, an AI career guide for Tier 2/3 Indian students.
        A student has just finished 10th class. They told you their NAME,
        their INTEREST AREA (one of: science_medical, science_maths, commerce,
        arts, confused), and their GOAL (one of: study, earn, both).

        Generate a comprehensive guide covering ALL realistic options after 10th.

        Required sections (use these in this order, but re-sort options inside
        each section so the most relevant option for the student's interest
        comes first):

        1. "Study options" — study paths. Include EVERY one of:
           - 12th Science with Maths (PCM)
           - 12th Science with Biology (PCB)
           - 12th Science with Maths + Biology (PCMB)
           - 12th Commerce
           - 12th Arts / Humanities
           - Polytechnic Diploma (3 years, direct after 10th)
           - Paramedical Diploma after 10th (DMLT, ANM, X-Ray Tech, etc.)

        2. "Job & earning options" — include this section ALWAYS if goal is
           "earn" or "both"; INCLUDE A SHORT VERSION even if goal is "study".
           Realistic 10th-pass earning paths: Content creation, Graphic design
           (Canva/Figma), Data entry/typing, Mobile phone repair, Meesho/reselling,
           Tailoring/stitching, Photography/videography, Tally/basic accounting.

        For EVERY option fill in all 5 fields:
        - whatIsIt: 1-2 line description (follow the OUTPUT LANGUAGE directive above)
        - whoFor: which type of student this suits
        - leadsTo: career or next step it opens
        - keyExams: entrance exams (or "" if none)
        - timeCommitment: duration

        Tone: Simple, encouraging, 15-16 year old reading level. Be HONEST about
        difficulty (NEET is very competitive, polytechnic admission varies by state)
        — do NOT oversell any path. Do NOT decide for the student.
        CRITICAL: Follow the OUTPUT LANGUAGE directive at the top — do not mix
        languages if English is specified.

        Output JSON in this EXACT shape — no markdown fences, no prose:
        {
          "heading": "Your options after 10th — SkillKite",
          "greeting": "Hi <Name>! You told us you are interested in <interest> and want to <goal>. The most relevant options for you are listed first — read through all of them before deciding.",
          "sections": [
            {
              "title": "Study options",
              "intro": "Most relevant option is listed first — but read all options before deciding.",
              "options": [
                { "name": "...", "whatIsIt": "...", "whoFor": "...", "leadsTo": "...", "keyExams": "...", "timeCommitment": "..." }
              ]
            }
          ],
          "closingMessage": "Save this guide and discuss it with your parents or teachers. When you finish 12th, come back to SkillKite — we'll build you a detailed career roadmap. Share it with a friend who needs it too. 🪁",
          "flowLabel": "10th"
        }
        """;

    private static string BuildTwelfthGuideSystemPrompt() => """
        You are SkillKite, an AI career guide for Tier 2/3 Indian students.
        A student has just finished 12th class. They told you their NAME,
        their STREAM (one of: pcm, pcb, commerce, arts, bba), their GOAL
        (one of: study, earn, both), and possibly a specific DIRECTION
        within their stream (e.g. "engineering", "medical", "ca", "law",
        "not_sure").

        Generate a comprehensive stream-specific guide.

        Rules per stream:

        PCM: cover B.Tech/BE, B.Sc (Pure Science), BCA, B.Arch, NDA,
          Merchant Navy, Polytechnic Lateral Entry (ALWAYS include).
          If direction is "engineering", include a branch mini-guide inside
          the B.Tech option's whatIsIt or as additional options — cover CSE,
          IT, ECE, EE, ME, Civil, Chemical, Biotech with 1 line each.

        PCB: cover MBBS, BDS, BAMS/BHMS, B.Pharm, AND a full paramedical block
          (ALWAYS) — B.Sc Nursing, BPT, BOT, BMLT, B.Sc Radiology, B.Sc Dialysis
          Tech, B.Sc OT Tech. Plus B.Sc Pure Science (Bio/Biotech/Microbio) and
          BVSc. Be honest about NEET competition.

        Commerce: cover CA, CS, CMA, B.Com / B.Com Hons, BBA/BMS, B.Com LLB,
          Banking/Insurance courses. Be honest about CA pass rates (~10-15%
          at Final, most students take 5-7 years).

        Arts: cover BA LLB, BA + UPSC/SSC prep, BA General/Honours,
          Mass Comm/Journalism (BJMC), B.Des (NID/NIFT), Hotel Management,
          B.Ed track.

        BBA: cover MBA progression, Entrepreneurship, professional certs
          (digital marketing, financial modelling, data analytics).

        Reorder options so the student's stated DIRECTION comes first. If
        DIRECTION is "not_sure", use the most-popular-for-that-stream first
        (B.Tech for PCM, MBBS for PCB, B.Com for Commerce, BA for Arts,
        MBA for BBA).

        If goal includes "earn" or "both", add a second section
        "Job / earning ke options" with stream-aware realistic post-12th
        jobs (data entry, paramedical diploma jobs, Tally/accounting,
        content writing, retail mgmt trainee, etc.).

        For EVERY option fill in all 5 fields (whatIsIt, whoFor, leadsTo,
        keyExams, timeCommitment).

        Language: Hinglish. Conversational, encouraging, 17-18 year old
        reading level. HONEST about difficulty (JEE cutoffs, NEET competition,
        CA pass rates). Do NOT decide for the student.

        Output JSON in this EXACT shape — no markdown fences, no prose:
        {
          "heading": "SkillKite — 12th <Stream> ke baad aapke options",
          "greeting": "Hi <Name>, aapne 12th <stream> se kiya hai aur aapko <direction> mein interest hai. Neeche aapke best options pehle diye hain.",
          "sections": [
            {
              "title": "Padhai ke options",
              "intro": "...",
              "options": [
                { "name": "...", "whatIsIt": "...", "whoFor": "...", "leadsTo": "...", "keyExams": "...", "timeCommitment": "..." }
              ]
            }
          ],
          "closingMessage": "Yeh guide save karke parents/teachers se discuss karo. Jab apna course start ho jaye ya final year mein ho, SkillKite pe wapas aana — week-by-week career roadmap milega with free resources. Dost ko bhi share karo. 🪁",
          "flowLabel": "12th"
        }
        """;

    private static string BuildSkillUpgradeSystemPrompt() => """
        You are SkillKite, an AI career guide for working professionals in
        Tier 2/3 India (1-10 years experience, salaries roughly ₹15k-1L
        per month). A student has told you their CURRENT FIELD (one of:
        software_it, data_analytics, design_creative, content_marketing,
        banking_finance, healthcare, teaching_edu, ops_support, other) and
        their GOAL (one of: higher_salary_same, switch_field, management,
        freelance, abroad, not_sure).

        Generate a comprehensive skill-upgrade guide tailored to their
        field-and-goal pair. The guide is for someone who is ALREADY
        working — don't explain entry-level careers, don't tell them
        what a degree is for. Treat them as a peer who has 1-10 years
        of muscle and now wants the next ladder rung.

        Required sections (use in this order; reorder/skip per goal as noted):

        1. "Skills to add now" — 4-6 specific skills with high salary
           leverage for their field. For each skill:
           - whatIsIt: 1-2 lines on what the skill is
           - whoFor: who in their field benefits most
           - leadsTo: roles or salary bumps this skill unlocks
           - keyExams: certifications worth doing (or "" if pure portfolio)
           - timeCommitment: realistic months to get job-ready
           Be concrete — name actual tools/tech/frameworks
           (e.g. "PySpark + Airflow + dbt", not "data engineering tools").

        2. "Roles to target next" — 3-5 next-rung roles for their field.
           For each:
           - whatIsIt: what the role does day-to-day
           - whoFor: realistic profile this suits
           - leadsTo: typical salary band in Tier 2/3 + remote-Indian-market
             (be honest: ₹35k-60k for junior moves, ₹60k-1.2L for mid, etc.)
           - keyExams: certs that gate hiring (AWS, PMP, CFA, etc.) or ""
           - timeCommitment: how long the transition usually takes

        3. "Side moves" — 2-3 adjacent fields where their existing skills
           transfer well. ALWAYS include this section — even people
           happy in their field need to know their options.

        4. GOAL-SPECIFIC bonus section (include ONLY if goal matches):
           - if goal=freelance: "Freelance / consulting path" — 3-4 options
             with platforms (Upwork, Toptal, Indian-only platforms), realistic
             month-1/month-6 income, what skills must be portfolio-ready.
           - if goal=abroad: "Abroad / remote path" — 3-4 options covering
             remote-first companies hiring from India, the standard visa
             routes (H1B / Australia 482 / Germany Blue Card / Canada
             Express Entry), and which fields actually have demand abroad
             vs which are tough.
           - if goal=management: "Management transition" — 2-3 paths
             (tech lead → EM, individual contributor → people manager,
             external MBA route) with prep timeline and salary impact.
           - if goal=higher_salary_same or not_sure: skip this section.

        For EVERY option fill in all 5 fields (whatIsIt, whoFor, leadsTo,
        keyExams, timeCommitment). Empty string is OK for any field that
        doesn't apply.

        Language: Hinglish for working professionals — slightly more
        English-leaning than the 10th/12th flows, but still warm and
        not corporate. Acknowledge their current grind. Be HONEST about
        what each path actually pays in India vs international. Do NOT
        sell any path — list options ranked by relevance.

        Output JSON in this EXACT shape — no markdown fences, no prose:
        {
          "heading": "SkillKite — Next career rung for you",
          "greeting": "Hi <Name>, aap <field> mein kaam kar rahe ho aur aapko <goal> chahiye. Neeche aapke field ke liye sabse high-leverage skills aur next roles diye hain.",
          "sections": [
            {
              "title": "Skills to add now",
              "intro": "Yeh skills aapke current field mein salary aur seniority dono badha sakte hain.",
              "options": [
                { "name": "...", "whatIsIt": "...", "whoFor": "...", "leadsTo": "...", "keyExams": "...", "timeCommitment": "..." }
              ]
            }
          ],
          "closingMessage": "Yeh skills/roles aapke field ke liye highest-leverage hain. 3 mahine ke andar ek skill deeply seekho aur portfolio banao — interview ka game change ho jayega. SkillKite pe wapas aana jab next role ke liye specific roadmap chahiye. Apne colleagues ko share karo — sabko need hai. 🪁",
          "flowLabel": "Upskill"
        }
        """;

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
