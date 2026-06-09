namespace SkillKite.Core.Enums;

public enum SessionStatus
{
    Active,
    /// <summary>
    /// New student just said "Hi". Bot has sent the 4-option entry menu
    /// (10th / 12th / Career / Skill upgrade). Next inbound message is
    /// treated as the flow choice and routed accordingly.
    /// </summary>
    AwaitingFlowChoice,
    /// <summary>
    /// Assessment is fully answered. Bot has suggested 3 career paths.
    /// Next inbound message is treated as the student's career selection.
    /// </summary>
    AwaitingCareerChoice,
    /// <summary>
    /// Returning student (gap &gt; the post-roadmap window) just messaged. We sent
    /// them a 3-button welcome-back menu (chat existing / fresh options / full
    /// re-assessment). Next inbound message routes to the chosen path.
    /// </summary>
    AwaitingReturnChoice,
    /// <summary>
    /// PDF / guide just delivered. Bot sent 3 feedback buttons
    /// (👍 Useful / 😐 OK / 👎 Not useful). Next inbound message is interpreted
    /// as either a button tap (saved as rating) or free text (saved as "Skipped"
    /// rating + routed to post-roadmap chat). Session moves to Completed after
    /// the rating is captured.
    /// </summary>
    AwaitingFeedback,
    Completed,
    Abandoned
}

/// <summary>
/// What a student said about their generated roadmap/guide via the post-PDF
/// 3-button prompt. Stored in ChatSession.AssessmentDataJson under
/// "feedbackRating" so we don't have to alter the schema.
/// </summary>
public enum FeedbackRating
{
    Useful,
    Ok,
    NotUseful,
    /// <summary>Student ignored the buttons (e.g. typed something else, or never replied).</summary>
    Skipped
}

public enum MessageRole { User, Assistant, System }
public enum RoadmapStatus { Active, Completed, Abandoned }
public enum ProgressStatus { Pending, InProgress, Completed, Skipped }
public enum CareerCategory { Tech, Government, Creative, Trades, Gig, Emerging }
public enum DemandLevel { Low, Medium, High }
public enum PreferredLanguage { Hindi, English }
