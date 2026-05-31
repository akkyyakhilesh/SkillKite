namespace SkillKite.Infrastructure.AI;

/// <summary>
/// Curated assessment script. The Claude engine uses these as anchor questions
/// but is free to rephrase, ask clarifying follow-ups, or skip questions whose
/// answers were already volunteered.
/// </summary>
public static class AssessmentQuestions
{
    public record Question(string Key, string English, string Hindi);

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
        new("remoteOk",        "Are you open to working remotely, or do you prefer a local job?",
                               "क्या आप रिमोट काम करने को तैयार हैं, या लोकल जॉब चाहते हैं?"),
        new("govtInterest",    "Are you interested in government jobs (SSC, banking, railways, etc.)?",
                               "क्या आप सरकारी नौकरी में रुचि रखते हैं (SSC, बैंक, रेलवे)?"),
        new("familyExpect",    "What does your family expect — a job right away, or further studies?",
                               "घरवाले क्या चाहते हैं — तुरंत नौकरी, या आगे की पढ़ाई?"),
        new("dailyHours",      "How much time can you dedicate to learning daily? 1 hr, 2-3 hrs, or full-time?",
                               "रोज़ कितना समय सीखने के लिए दे सकते हैं? 1 घंटा, 2-3 घंटे, या पूरा दिन?"),
        new("device",          "Do you have a laptop, or only a phone?",
                               "आपके पास लैपटॉप है या सिर्फ़ फ़ोन?"),
        new("salaryGoal",      "What monthly salary would make you and your family feel successful?",
                               "कितनी monthly सैलरी आपको और घरवालों को satisfied लगेगी?")
    };
}
