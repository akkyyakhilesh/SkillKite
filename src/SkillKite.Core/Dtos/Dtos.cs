namespace SkillKite.Core.Dtos;

public record AssessmentTurnResult(
    string ReplyText,
    bool IsComplete,
    Dictionary<string, string>? ExtractedFields);

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
