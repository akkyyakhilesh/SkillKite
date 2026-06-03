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

        new("interests",       "What subjects or activities do you enjoy most?",
                               "आपको कौन से विषय या काम सबसे ज़्यादा पसंद हैं?"),

        new("skills",          "Do you have any skills already — coding, design, writing, speaking, anything?",
                               "आपके पास कोई स्किल है क्या — कोडिंग, डिज़ाइन, लिखना, बोलना, कुछ भी?"),

        new("experience",      "Have you done any internships, projects, or freelance work?",
                               "क्या आपने कोई इंटर्नशिप, प्रोजेक्ट या फ़्रीलांस काम किया है?"),

        // workType — replaces the old "remoteOk" question.
        // Differentiates roadmap output: full-time path vs freelance path vs both.
        new("workType",        "Aap kya dhundh rahe ho — full-time job, ya freelance / side work?",
                               "आप क्या ढूँढ रहे हैं — full-time job, ya freelance / side work?",
                               InteractiveKind.Buttons,
                               new[]
                               {
                                   new InteractiveOption("full_time", "💼 Work full time"),
                                   new InteractiveOption("freelance", "🌱 Freelance / side"),
                                   new InteractiveOption("both",      "🤔 Dono open")
                               }),

        new("govtInterest",    "Are you interested in government jobs (SSC, banking, railways, etc.)?",
                               "क्या आप सरकारी नौकरी में रुचि रखते हैं (SSC, बैंक, रेलवे)?",
                               InteractiveKind.Buttons,
                               new[]
                               {
                                   new InteractiveOption("yes",  "✅ Haan, interest hai"),
                                   new InteractiveOption("no",   "❌ Nahi"),
                                   new InteractiveOption("open", "🤷 Dono open")
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

        new("dailyHours",      "How much time can you dedicate to learning daily? 1 hr, 2-3 hrs, or full-time?",
                               "रोज़ कितना समय सीखने के लिए दे सकते हैं? 1 घंटा, 2-3 घंटे, या पूरा दिन?",
                               InteractiveKind.Buttons,
                               new[]
                               {
                                   new InteractiveOption("1h",       "⏱ 1 ghanta"),
                                   new InteractiveOption("2-3h",     "⏰ 2-3 ghante"),
                                   new InteractiveOption("fulltime", "🔥 Full-time")
                               }),

        new("device",          "Do you have a laptop, or only a phone?",
                               "आपके पास लैपटॉप है या सिर्फ़ फ़ोन?",
                               InteractiveKind.Buttons,
                               new[]
                               {
                                   new InteractiveOption("phone",  "📱 Sirf phone"),
                                   new InteractiveOption("laptop", "💻 Laptop hai"),
                                   new InteractiveOption("both",   "📱💻 Dono")
                               }),

        // Salary as a list message — 5 anchored ranges + a custom escape hatch.
        // Anchoring prevents unrealistic fresher targets (e.g. ₹80k entry-level CSE
        // in Bhagalpur) and gives Claude clean signal to calibrate the roadmap.
        new("salaryGoal",      "What monthly salary would make you and your family feel successful?",
                               "कितनी monthly सैलरी आपको और घरवालों को satisfied लगेगी?",
                               InteractiveKind.List,
                               new[]
                               {
                                   new InteractiveOption("10-15k",  "₹10–15k / month",  "Starting out, learning fast"),
                                   new InteractiveOption("15-25k",  "₹15–25k / month",  "Typical first-job target"),
                                   new InteractiveOption("25-40k",  "₹25–40k / month",  "Strong first-job target"),
                                   new InteractiveOption("40-60k",  "₹40–60k / month",  "Above-average entry"),
                                   new InteractiveOption("60k+",    "₹60k+ / month",    "Ambitious — specialized skills"),
                                   new InteractiveOption("custom",  "✏️ Type my own",   "Reply with a specific number")
                               },
                               ListButtonLabel: "Select range",
                               ListSectionTitle: "Monthly salary goal"),

        // ASKED LAST — right before the roadmap is generated. Determines PDF language.
        new("roadmapLanguage", "Last cheez — roadmap kis language mein bheju? Hindi ya English?",
                               "एक आख़िरी बात — रोडमैप किस language में भेजूँ? Hindi ya English?",
                               InteractiveKind.Buttons,
                               new[]
                               {
                                   new InteractiveOption("hindi",   "🇮🇳 हिंदी"),
                                   new InteractiveOption("english", "🇬🇧 English")
                               })
    };

    public static Question? ByKey(string key) =>
        All.FirstOrDefault(q => q.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
}
