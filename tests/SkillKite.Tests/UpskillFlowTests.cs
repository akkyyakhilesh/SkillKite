using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SkillKite.Core.Dtos;
using SkillKite.Core.Enums;
using SkillKite.Core.Interfaces;
using SkillKite.Core.Models;
using SkillKite.Data;
using SkillKite.Infrastructure.AI;
using Xunit;

namespace SkillKite.Tests;

/// <summary>
/// Drives the upskill flow end-to-end through the real AssessmentOrchestrator
/// against an in-memory DB, with fakes for messaging / engine / pdf. Verifies
/// the four June-15 improvements: language-first ordering, tech-stack question,
/// multi-select goals, and the top-3 summary before the PDF.
/// </summary>
public class UpskillFlowTests
{
    // ---- Fakes -------------------------------------------------------------

    /// <summary>Records every outbound message so tests can assert on the script.</summary>
    private sealed class FakeMessaging : IMessagingService
    {
        public record Sent(string Kind, string Body, IReadOnlyList<InteractiveOption>? Options);
        public List<Sent> Log { get; } = new();

        public Task SendTextAsync(string toPhone, string text, CancellationToken ct = default)
        { Log.Add(new("text", text, null)); return Task.CompletedTask; }

        public Task SendDocumentAsync(string toPhone, string url, string caption, string filename, CancellationToken ct = default)
        { Log.Add(new("document", caption, null)); return Task.CompletedTask; }

        public Task SendButtonsAsync(string toPhone, string body, IReadOnlyList<InteractiveOption> options, CancellationToken ct = default)
        { Log.Add(new("buttons", body, options)); return Task.CompletedTask; }

        public Task SendListAsync(string toPhone, string body, string buttonLabel, string sectionTitle, IReadOnlyList<InteractiveOption> options, CancellationToken ct = default)
        { Log.Add(new("list", body, options)); return Task.CompletedTask; }

        public bool VerifyWebhookSignature(string payload, string signatureHeader) => true;
        public IEnumerable<WhatsAppIncomingMessage> ParseIncoming(string payloadJson) => Array.Empty<WhatsAppIncomingMessage>();
    }

    /// <summary>Captures the session passed to GenerateSkillUpgradeGuideAsync so we can
    /// inspect what data the orchestrator handed to the engine (field, techStack, goals).</summary>
    private sealed class FakeEngine : ICareerEngine
    {
        public string? LastUpskillDataJson { get; private set; }

        public Task<StudentGuide> GenerateSkillUpgradeGuideAsync(Student student, ChatSession session, CancellationToken ct = default)
        {
            LastUpskillDataJson = session.AssessmentDataJson;
            var guide = new StudentGuide(
                Heading: "Your next career move — SkillKite",
                Greeting: $"Hi {student.Name}! Here are your highest-leverage moves.",
                Sections: new List<GuideSection>
                {
                    new("Skills to add now", "Top skills.", new List<GuideOption>
                    {
                        new("Azure cloud certs", "Cloud platform", "Backend/.NET devs", "Senior .NET roles at ₹18-30 LPA", "AZ-204", "3 months"),
                        new("System design", "Architecture", "Mid devs", "Lead roles", "", "4 months"),
                    }),
                    new("Roles to target next", "Next rungs.", new List<GuideOption>
                    {
                        new("Senior .NET Engineer", "Owns modules", "3-5 yr devs", "₹18-28 LPA", "", "6 months"),
                    }),
                    new("Side moves", "Adjacent.", new List<GuideOption>
                    {
                        new("Platform engineering", "DevOps-ish", "Infra-curious devs", "₹20-32 LPA", "", "6 months"),
                    }),
                },
                ClosingMessage: "Pick one skill, go deep. 🪁",
                FlowLabel: "Upskill");
            return Task.FromResult(guide);
        }

        // Unused in upskill flow — throw so an accidental call is loud.
        public Task<AssessmentTurnResult> NextTurnAsync(Student s, ChatSession se, IReadOnlyList<ChatMessage> h, string? m, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<CareerSuggestionsResult> SuggestCareerPathsAsync(Student s, ChatSession se, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<GeneratedRoadmap> GenerateRoadmapAsync(Student s, ChatSession se, string? c = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<PostRoadmapTurnResult> PostRoadmapTurnAsync(Student s, GeneratedRoadmap r, IReadOnlyList<ChatMessage> h, string m, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<StudentGuide> GenerateTenthGuideAsync(Student s, ChatSession se, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<StudentGuide> GenerateTwelfthGuideAsync(Student s, ChatSession se, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class FakePdf : IRoadmapGenerator
    {
        public Task<string> GenerateAsync(Student s, GeneratedRoadmap r, CancellationToken ct = default) => Task.FromResult("https://x/roadmap.pdf");
        public Task<string> GenerateGuideAsync(Student s, StudentGuide g, CancellationToken ct = default) => Task.FromResult("https://x/guide.pdf");
        public Task DeletePdfsForStudentAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
        public (string Url, string Filename)? FindLatestPdfForStudent(Guid id) => null;
    }

    // ---- Harness -----------------------------------------------------------

    private static (AssessmentOrchestrator orch, FakeMessaging msg, FakeEngine eng, AppDbContext db) NewHarness()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"skillkite-{Guid.NewGuid()}")
            .Options;
        var db = new AppDbContext(opts);
        var msg = new FakeMessaging();
        var eng = new FakeEngine();
        var orch = new AssessmentOrchestrator(db, eng, new FakePdf(), msg, NullLogger<AssessmentOrchestrator>.Instance);
        return (orch, msg, eng, db);
    }

    // ---- Tests -------------------------------------------------------------

    [Fact]
    public async Task NewStudent_IsAskedLanguageBeforeFlowMenu()
    {
        var (orch, msg, _, _) = NewHarness();

        await orch.HandleIncomingAsync("91900000001", "Hi", "Ramya");

        // First outbound must be the language prompt (buttons), NOT the flow menu.
        var first = msg.Log[0];
        Assert.Equal("buttons", first.Kind);
        Assert.Contains("language", first.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(first.Options!, o => o.Id == "lang_english");
        Assert.Contains(first.Options!, o => o.Id == "lang_hinglish");
        // And no flow menu yet.
        Assert.DoesNotContain(msg.Log, m => m.Options?.Any(o => o.Id == "flow_upskill") == true);
    }

    [Fact]
    public async Task PickingEnglish_ThenShowsFlowMenuInEnglish()
    {
        var (orch, msg, _, _) = NewHarness();

        await orch.HandleIncomingAsync("91900000002", "Hi", "Ramya");
        await orch.HandleIncomingAsync("91900000002", "lang_english", "Ramya");

        var flowMenu = msg.Log.Last();
        Assert.Equal("list", flowMenu.Kind);
        Assert.Contains(flowMenu.Options!, o => o.Id == "flow_upskill");
        Assert.Contains("What would you like help with", flowMenu.Body);
    }

    [Fact]
    public async Task FlowMenu_DoesNotRepeatNameOrIntroAfterLanguagePrompt()
    {
        // Regression: the flow menu used to re-greet "Hi {name}! I'm SkillKite…"
        // right after the language prompt already did — a double greeting.
        var (orch, msg, _, _) = NewHarness();

        await orch.HandleIncomingAsync("91900000010", "Hi", "Ramya");   // usable name
        var langPrompt = msg.Log.First(m => m.Kind == "buttons");
        await orch.HandleIncomingAsync("91900000010", "lang_english", "Ramya");
        var flowMenu = msg.Log.Last();

        // Name appears in the language prompt…
        Assert.Contains("Ramya", langPrompt.Body);
        // …but NOT again in the flow menu, and no repeated "I'm SkillKite" intro.
        Assert.DoesNotContain("Ramya", flowMenu.Body);
        Assert.DoesNotContain("I'm SkillKite", flowMenu.Body);
    }

    [Fact]
    public async Task UnusableWhatsAppName_AsksNameBeforeFlowMenu()
    {
        // "S D" is not a usable name (no 2 contiguous letters). The greeting must
        // NOT show it, and we ask the name BEFORE the 4-path menu — centrally.
        var (orch, msg, _, _) = NewHarness();
        const string phone = "91900000011";

        await orch.HandleIncomingAsync(phone, "Hi", "S D");
        var langPrompt = msg.Log.First(m => m.Kind == "buttons");
        Assert.DoesNotContain("S D", langPrompt.Body);
        Assert.Contains("Hi! 🪁", langPrompt.Body);   // generic, no name

        await orch.HandleIncomingAsync(phone, "lang_english", "S D");

        // Name is asked right after language — before any flow list appears.
        var afterLang = msg.Log.Last();
        Assert.Equal("text", afterLang.Kind);
        Assert.Contains("what's your name", afterLang.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(msg.Log, m => m.Options?.Any(o => o.Id == "flow_upskill") == true);

        // After typing a name, the 4-path menu appears with a one-time greeting.
        await orch.HandleIncomingAsync(phone, "Shristi", "S D");
        var flowMenu = msg.Log.Last();
        Assert.Equal("list", flowMenu.Kind);
        Assert.Contains(flowMenu.Options!, o => o.Id == "flow_upskill");
        Assert.Contains("Shristi", flowMenu.Body);
    }

    [Fact]
    public async Task CentralName_ThenUpskill_DoesNotAskNameAgain()
    {
        // The whole point: name collected before the path choice means the
        // upskill flow must NOT ask it a second time.
        var (orch, msg, _, _) = NewHarness();
        const string phone = "91900000013";

        await orch.HandleIncomingAsync(phone, "Hi", "S D");
        await orch.HandleIncomingAsync(phone, "lang_english", "S D");
        await orch.HandleIncomingAsync(phone, "Shristi", "S D");   // central name
        msg.Log.Clear();
        await orch.HandleIncomingAsync(phone, "flow_upskill", "S D");

        Assert.DoesNotContain(msg.Log, m => m.Body.Contains("what's your name", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(msg.Log, m => m.Body.Contains("Shristi"));   // greets by collected name
    }

    [Fact]
    public async Task UsableWhatsAppName_SkipsNameQuestionEntirely()
    {
        // A good WhatsApp name should be used and the name question skipped —
        // no central prompt, straight to the menu, straight to the field selector.
        var (orch, msg, _, _) = NewHarness();
        const string phone = "91900000012";

        await orch.HandleIncomingAsync(phone, "Hi", "Ramya");
        await orch.HandleIncomingAsync(phone, "lang_english", "Ramya");
        // No name prompt between language and the flow menu.
        Assert.Equal("list", msg.Log.Last().Kind);
        Assert.Contains(msg.Log.Last().Options!, o => o.Id == "flow_upskill");

        await orch.HandleIncomingAsync(phone, "flow_upskill", "Ramya");
        Assert.DoesNotContain(msg.Log, m => m.Body.Contains("what's your name", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(msg.Log, m => m.Kind == "list" && m.Options!.Any(o => o.Id == "software_it"));
    }

    [Fact]
    public async Task SoftwareField_TriggersTechStackQuestion()
    {
        var (orch, msg, _, _) = NewHarness();
        const string phone = "91900000003";

        await orch.HandleIncomingAsync(phone, "Hi", "Ramya");
        await orch.HandleIncomingAsync(phone, "lang_english", "Ramya");
        await orch.HandleIncomingAsync(phone, "flow_upskill", "Ramya");
        await orch.HandleIncomingAsync(phone, "software_it", "Ramya");

        // The most recent message should be the tech-stack free-text prompt.
        var last = msg.Log.Last();
        Assert.Equal("text", last.Kind);
        Assert.Contains("tech stack", last.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NonTechField_SkipsTechStackQuestion()
    {
        var (orch, msg, _, _) = NewHarness();
        const string phone = "91900000004";

        await orch.HandleIncomingAsync(phone, "Hi", "Ramya");
        await orch.HandleIncomingAsync(phone, "lang_english", "Ramya");
        await orch.HandleIncomingAsync(phone, "flow_upskill", "Ramya");
        await orch.HandleIncomingAsync(phone, "healthcare", "Ramya");

        // Healthcare → straight to the goal list, no tech-stack text prompt.
        var last = msg.Log.Last();
        Assert.Equal("list", last.Kind);
        Assert.Contains(last.Options!, o => o.Id == "higher_salary_same");
        Assert.DoesNotContain(msg.Log, m => m.Body.Contains("tech stack", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task MultiSelectGoals_AccumulateAndDone_GeneratesGuide()
    {
        var (orch, msg, eng, _) = NewHarness();
        const string phone = "91900000005";

        await orch.HandleIncomingAsync(phone, "Hi", "Ramya");
        await orch.HandleIncomingAsync(phone, "lang_english", "Ramya");
        await orch.HandleIncomingAsync(phone, "flow_upskill", "Ramya");
        await orch.HandleIncomingAsync(phone, "software_it", "Ramya");
        await orch.HandleIncomingAsync(phone, ".NET, Azure, SQL Server", "Ramya");
        await orch.HandleIncomingAsync(phone, "higher_salary_same", "Ramya");  // first goal
        await orch.HandleIncomingAsync(phone, "abroad", "Ramya");              // second goal
        await orch.HandleIncomingAsync(phone, "goal_done", "Ramya");           // finish

        // Engine got both goals + the tech stack.
        Assert.NotNull(eng.LastUpskillDataJson);
        Assert.Contains("higher_salary_same", eng.LastUpskillDataJson!);
        Assert.Contains("abroad", eng.LastUpskillDataJson!);
        Assert.Contains(".NET, Azure, SQL Server", eng.LastUpskillDataJson!);

        // A PDF document was delivered.
        Assert.Contains(msg.Log, m => m.Kind == "document");
    }

    [Fact]
    public async Task Top3Summary_IsSentBeforeThePdf()
    {
        var (orch, msg, _, _) = NewHarness();
        const string phone = "91900000006";

        await orch.HandleIncomingAsync(phone, "Hi", "Ramya");
        await orch.HandleIncomingAsync(phone, "lang_english", "Ramya");
        await orch.HandleIncomingAsync(phone, "flow_upskill", "Ramya");
        await orch.HandleIncomingAsync(phone, "software_it", "Ramya");
        await orch.HandleIncomingAsync(phone, ".NET, Azure", "Ramya");
        await orch.HandleIncomingAsync(phone, "higher_salary_same", "Ramya");
        await orch.HandleIncomingAsync(phone, "goal_done", "Ramya");

        var summaryIdx = msg.Log.FindIndex(m => m.Body.Contains("top 3", StringComparison.OrdinalIgnoreCase));
        var docIdx = msg.Log.FindIndex(m => m.Kind == "document");

        Assert.True(summaryIdx >= 0, "Expected a top-3 summary message");
        Assert.True(docIdx >= 0, "Expected a PDF document");
        Assert.True(summaryIdx < docIdx, "Top-3 summary should come before the PDF");

        // The summary should name the top pick from the first guide section.
        Assert.Contains("Azure cloud certs", msg.Log[summaryIdx].Body);
    }

    [Fact]
    public async Task ResetPrompt_RespectsSelectedLanguage()
    {
        // English user: body AND button labels should be English.
        var (orch, msg, _, _) = NewHarness();
        const string phone = "91900000008";

        await orch.HandleIncomingAsync(phone, "Hi", "Ramya");
        await orch.HandleIncomingAsync(phone, "lang_english", "Ramya");
        msg.Log.Clear();

        await orch.HandleIncomingAsync(phone, "reset", "Ramya");

        var prompt = msg.Log.Single(m => m.Kind == "buttons");
        Assert.Contains("delete ALL your SkillKite data", prompt.Body);
        Assert.Contains(prompt.Options!, o => o.Id == "reset_yes" && o.Title.Contains("Yes"));
        Assert.Contains(prompt.Options!, o => o.Id == "reset_no" && o.Title.Contains("No"));
        Assert.DoesNotContain(prompt.Options!, o => o.Title.Contains("Haan") || o.Title.Contains("Nahi"));
    }

    [Fact]
    public async Task ResetPrompt_HinglishUser_GetsHinglishButtons()
    {
        var (orch, msg, _, _) = NewHarness();
        const string phone = "91900000009";

        await orch.HandleIncomingAsync(phone, "Hi", "Ramya");
        await orch.HandleIncomingAsync(phone, "lang_hinglish", "Ramya");
        msg.Log.Clear();

        await orch.HandleIncomingAsync(phone, "reset", "Ramya");

        var prompt = msg.Log.Single(m => m.Kind == "buttons");
        Assert.Contains(prompt.Options!, o => o.Id == "reset_yes" && o.Title.Contains("Haan"));
        Assert.Contains(prompt.Options!, o => o.Id == "reset_no" && o.Title.Contains("Nahi"));
    }

    [Fact]
    public async Task NotSureGoal_AutoCompletesWithoutFollowUp()
    {
        var (orch, msg, eng, _) = NewHarness();
        const string phone = "91900000007";

        await orch.HandleIncomingAsync(phone, "Hi", "Ramya");
        await orch.HandleIncomingAsync(phone, "lang_english", "Ramya");
        await orch.HandleIncomingAsync(phone, "flow_upskill", "Ramya");
        await orch.HandleIncomingAsync(phone, "healthcare", "Ramya");
        await orch.HandleIncomingAsync(phone, "not_sure", "Ramya");

        // "Show me all" should generate immediately — no "anything else?" follow-up.
        Assert.NotNull(eng.LastUpskillDataJson);
        Assert.Contains("not_sure", eng.LastUpskillDataJson!);
        Assert.Contains(msg.Log, m => m.Kind == "document");
    }
}
