namespace SkillKite.Core.Enums;

public enum SessionStatus
{
    Active,
    /// <summary>
    /// Assessment is fully answered. Bot has suggested 3 career paths.
    /// Next inbound message is treated as the student's career selection.
    /// </summary>
    AwaitingCareerChoice,
    Completed,
    Abandoned
}
public enum MessageRole { User, Assistant, System }
public enum RoadmapStatus { Active, Completed, Abandoned }
public enum ProgressStatus { Pending, InProgress, Completed, Skipped }
public enum CareerCategory { Tech, Government, Creative, Trades, Gig, Emerging }
public enum DemandLevel { Low, Medium, High }
public enum PreferredLanguage { Hindi, English }
