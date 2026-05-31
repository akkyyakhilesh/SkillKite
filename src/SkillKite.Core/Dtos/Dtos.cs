namespace SkillKite.Core.Dtos;

public record AssessmentTurnResult(
    string ReplyText,
    bool IsComplete,
    Dictionary<string, string>? ExtractedFields);

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
