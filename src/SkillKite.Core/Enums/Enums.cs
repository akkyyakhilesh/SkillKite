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
    /// New student just picked their flow (10th / 12th / Career / Upskill).
    /// Before kicking off the first real question, we asked which language
    /// they want — English or Hinglish — and parked the session here.
    /// The flow they chose is stashed in AssessmentDataJson under
    /// "pendingFlow" so HandleLanguageChoiceAsync can dispatch correctly.
    /// </summary>
    AwaitingLanguageChoice,
    /// <summary>
    /// PDF / guide just delivered. Bot sent 3 feedback buttons
    /// (👍 Useful / 😐 OK / 👎 Not useful). Next inbound message is interpreted
    /// as either a button tap (saved as rating) or free text (saved as "Skipped"
    /// rating + routed to post-roadmap chat). Session moves to Completed after
    /// the rating is captured.
    /// </summary>
    AwaitingFeedback,
    /// <summary>
    /// Student typed a "delete my data" / "reset" intent. Bot sent 2 buttons
    /// (✅ Yes, delete / ❌ No, keep). On Yes, we wipe their ChatMessages,
    /// ChatSessions, Roadmaps, and the Student row itself (cascading) + their
    /// PDFs on disk. On No or any other free text, we abandon this transient
    /// session and fall back to normal dispatch.
    /// </summary>
    AwaitingResetConfirm,
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
/// <summary>
/// What language the bot uses with the student. Renamed from `Hindi` to
/// `Hinglish` on 2026-06-09 — the original "Hindi" value rendered Devanagari
/// chrome which Tier 2/3 users found heavier than helpful. The enum's
/// underlying integer value is unchanged (Hinglish=0), so existing rows that
/// stored `Hindi` are now interpreted as `Hinglish` — closer to what those
/// students were actually getting in chat anyway. Pure Hindi can be added
/// back as a third value if real users explicitly ask for it.
/// </summary>
public enum PreferredLanguage { Hinglish, English }
