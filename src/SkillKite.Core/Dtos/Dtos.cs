namespace SkillKite.Core.Dtos;

public record AssessmentTurnResult(
    string ReplyText,
    bool IsComplete,
    Dictionary<string, string>? ExtractedFields,
    InteractiveBlock? Interactive = null);

/// <summary>
/// One of three career paths the engine suggests after assessment completion.
/// The student picks one of these — only then do we generate the full roadmap.
/// Title is intentionally short (3-4 words) so it fits in a WhatsApp Reply
/// Button. The rationale is shown in the message body above the buttons.
/// </summary>
public record CareerSuggestion(
    string Id,        // short slug ("junior_web_dev", "content_writer")
    string Title,     // 3-4 word career name shown on the button
    string Rationale); // 1-line "why this fits you" reasoning

public record CareerSuggestionsResult(
    string IntroLine,                          // friendly preamble shown above the list
    IReadOnlyList<CareerSuggestion> Suggestions);

/// <summary>
/// One conversational turn AFTER the roadmap has been delivered. The bot stays
/// in chat mode (answering thanks, follow-up questions about the roadmap, etc.)
/// instead of restarting the assessment.
///
/// If the student explicitly asks for a fresh roadmap and confirms, the engine
/// sets <see cref="ShouldRestart"/> = true and the orchestrator creates a new
/// assessment session.
/// </summary>
public record PostRoadmapTurnResult(
    string ReplyText,
    bool ShouldRestart);

public record RoadmapWeek(
    int WeekNumber,
    string Theme,
    string ThemeHi,
    List<string> Goals,
    List<RoadmapResource> Resources,
    string Practice);

public record RoadmapResource(string Title, string Url, string Platform);

public record GeneratedRoadmap(
    string CareerTitle,
    string CareerTitleHi,
    string Summary,
    string SummaryHi,
    int TotalWeeks,
    int ExpectedSalaryMin,
    int ExpectedSalaryMax,
    List<RoadmapWeek> Weeks);

public record WhatsAppIncomingMessage(
    string From,
    string MessageId,
    string Text,
    string? ProfileName,
    DateTimeOffset Timestamp);

/// <summary>
/// One selectable option in a WhatsApp interactive message (Reply Button or
/// List Row). <see cref="Id"/> is what comes back to us in the webhook when
/// the student taps the option — and what Claude sees as the extracted answer.
/// Keep Id short, ASCII, and self-explanatory (e.g. "phone", "full_time").
/// </summary>
public record InteractiveOption(
    string Id,
    string Title,
    string? Description = null);

/// <summary>
/// When Claude wants the next turn rendered as buttons or a list (instead of
/// free text), it emits this block inside the assessment turn result.
/// The orchestrator reads it and calls the appropriate WhatsApp send method.
/// </summary>
public record InteractiveBlock(
    string Type,                              // "buttons" | "list"
    string Body,                              // message text shown above the options
    IReadOnlyList<InteractiveOption> Options, // 1-3 for buttons, 1-10 for list
    string? ButtonLabel = null,               // list only: text on the "open list" button
    string? SectionTitle = null);             // list only: section header
