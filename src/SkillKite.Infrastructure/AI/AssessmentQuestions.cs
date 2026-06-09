using SkillKite.Core.Dtos;

namespace SkillKite.Infrastructure.AI;

/// <summary>
/// Curated assessment script. Claude uses these as anchor questions but is
/// free to rephrase, ask clarifying follow-ups, or skip questions whose
/// answers were already volunteered.
///
/// For closed-enum questions (device, govtInterest, workType, etc.), each
/// <see cref="Question"/> carries a fixed option set. Claude is instructed to
/// emit an interactive block in the turn result for those — the orchestrator
/// then renders them as WhatsApp Buttons or a List Message.
///
/// Student can always bypass the buttons by typing a free-text reply.
/// </summary>
public static class AssessmentQuestions
{
    public record Question(
        string Key,
        string English,
        string Hindi,
        InteractiveKind Interactive = InteractiveKind.None,
        IReadOnlyList<InteractiveOption>? Options = null,
        string? ListButtonLabel = null,
        string? ListSectionTitle = null);

    public enum InteractiveKind
    {
        None,    // free text
        Buttons, // 2-3 quick-reply buttons
        List     // 1-10 row list message with optional descriptions
    }

    public static readonly IReadOnlyList<Question> All = new List<Question>
    {
        new("name",            "What's your name?",
                               "आपका नाम क्या है?"),

        new("education",       "What are you currently studying, or what was your last qualification?",
                               "आप अभी क्या पढ़ रहे हैं, या आपकी आख़िरी पढ़ाई क्या है?"),

        new("city",            "Which city or town are you from?",
                               "आप किस शहर या क़स्बे से हैं?"),

        // Anchored with examples — "subjects" + "activities" alone was too abstract;
        // testers (including the founder) didn't know what type of answer fit.
        new("interests",       "What do you enjoy doing most — coding, design, writing, speaking (sales/teaching), numbers, helping people, or something else?",
                               "आपको क्या करने में मज़ा आता है — coding, design, writing, बात करना (sales/teaching), numbers का काम, लोगों की help, या कुछ और?"),

        new("skills",          "Do you have any skills already — coding, design, writing, speaking, anything?",
                               "आपके पास कोई स्किल है क्या — कोडिंग, डिज़ाइन, लिखना, बोलना, कुछ भी?"),

        // 3 buttons cover the one useful distinction (real work vs academic only vs none).
        // The roadmap starts at "absolute basics" for "kuch nahi" and skips ahead for the rest.
        new("experience",      "Kya aapne koi internship, project, ya freelance kaam kiya hai ab tak?",
                               "क्या आपने कोई internship, project, या freelance काम किया है?",
                               InteractiveKind.Buttons,
                               new[]
                               {
                                   new InteractiveOption("real",    "💼 Internship / freelance"),
                                   new InteractiveOption("college", "📚 Sirf college project"),
                                   new InteractiveOption("none",    "❌ Kuch nahi abhi")
                               }),

        // Ordered: govtInterest BEFORE workType. If the student wants a govt job
        // (SSC / banking / railways), workType is implicitly "full_time" and Claude
        // can skip that question. Asking workType=freelance first and THEN asking
        // about govt jobs felt jarring — govt jobs are structurally full-time.
        new("govtInterest",    "Are you interested in government jobs (SSC, banking, railways, etc.)?",
                               "क्या आप सरकारी नौकरी में रुचि रखते हैं (SSC, बैंक, रेलवे)?",
                               InteractiveKind.Buttons,
                               new[]
                               {
                                   new InteractiveOption("yes",  "✅ Haan, interest hai"),
                                   new InteractiveOption("no",   "❌ Nahi"),
                                   new InteractiveOption("open", "🤷 Dono open")
                               }),

        // workType — replaces the old "remoteOk" question.
        // Differentiates roadmap output: full-time path vs freelance path vs both.
        // Skipped automatically by Claude when govtInterest=yes (govt = full-time).
        new("workType",        "Aap kya dhundh rahe ho — full-time job, ya freelance / side work?",
                               "आप क्या ढूँढ रहे हैं — full-time job, ya freelance / side work?",
                               InteractiveKind.Buttons,
                               new[]
                               {
                                   new InteractiveOption("full_time", "💼 Work full time"),
                                   new InteractiveOption("freelance", "🌱 Freelance / side"),
                                   new InteractiveOption("both",      "🤔 Dono open")
                               }),

        new("familyExpect",    "What does your family expect — a job right away, or further studies?",
                               "घरवाले क्या चाहते हैं — तुरंत नौकरी, या आगे की पढ़ाई?",
                               InteractiveKind.Buttons,
                               new[]
                               {
                                   new InteractiveOption("job",   "💼 Job jaldi"),
                                   new InteractiveOption("study", "📚 Aage padhna"),
                                   new InteractiveOption("both",  "🤔 Dono chal sakta")
                               }),

        // 4-5 ghante is the realistic upper bound for actual focused learning per day.
        // "Full-time" (8+ hrs) sounded ambitious but no student really sustains that —
        // even completely free students top out around 4-5 hrs of focused work. Setting
        // an honest ceiling here prevents the AI from calibrating roadmaps to
        // unrealistic time budgets.
        new("dailyHours",      "How much time can you dedicate to learning daily? 1 hr, 2-3 hrs, or 4-5 hrs?",
                               "रोज़ कितना समय सीखने के लिए दे सकते हैं? 1 घंटा, 2-3 घंटे, या 4-5 घंटे?",
                               InteractiveKind.Buttons,
                               new[]
                               {
                                   new InteractiveOption("1h",   "⏱ 1 ghanta"),
                                   new InteractiveOption("2-3h", "⏰ 2-3 ghante"),
                                   new InteractiveOption("4-5h", "🔥 4-5 ghante")
                               }),

        // Everyone using SkillKite has a phone (they're chatting on WhatsApp).
        // The real binary is whether they also have laptop access — so only
        // two buttons. "both" was redundant with "laptop" and just added cognitive load.
        new("device",          "Do you have a laptop too, or only a phone?",
                               "आपके पास laptop bhi hai, ya sirf phone?",
                               InteractiveKind.Buttons,
                               new[]
                               {
                                   new InteractiveOption("phone",  "📱 Sirf phone"),
                                   new InteractiveOption("laptop", "💻 Laptop bhi hai")
                               }),

        // 3 anchored buckets covering real Tier 2/3 expectations. A student who
        // wants a specific number can still type one instead of tapping. The
        // anchors prevent unrealistic fresher fantasies (Bhavesh asked for ₹80k
        // as a fresh B.Tech; bot quietly anchors to honest ranges).
        new("salaryGoal",      "What monthly salary would make you and your family feel successful?",
                               "कितनी monthly सैलरी आपको और घरवालों को satisfied लगेगी?",
                               InteractiveKind.Buttons,
                               new[]
                               {
                                   new InteractiveOption("10-25k", "₹10–25k / month"),
                                   new InteractiveOption("25-50k", "₹25–50k / month"),
                                   new InteractiveOption("50k+",   "₹50k+ / month")
                               })

        // NOTE: roadmapLanguage used to be the very last question (Hindi vs English).
        // As of 2026-06-09 we ask language UPFRONT, right after the flow choice menu
        // (see SendLanguageChoicePromptAsync in AssessmentOrchestrator), so the bot
        // chats in the student's chosen language end-to-end instead of asking only
        // for PDF rendering. The result is on Student.PreferredLanguage.
    };

    public static Question? ByKey(string key) =>
        All.FirstOrDefault(q => q.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
}
