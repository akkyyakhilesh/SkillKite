using SkillKite.Core.Dtos;
using SkillKite.Core.Models;

namespace SkillKite.Core.Interfaces;

public interface ICareerEngine
{
    /// <summary>
    /// Runs the next conversational assessment turn. Returns the reply to send
    /// back to the student, whether the assessment is complete, and any
    /// structured fields extracted from the student's latest reply.
    /// </summary>
    Task<AssessmentTurnResult> NextTurnAsync(
        ChatSession session,
        IReadOnlyList<ChatMessage> history,
        string? latestUserMessage,
        CancellationToken ct = default);

    /// <summary>
    /// Once assessment is complete, suggest the student's 3 best-fit career paths
    /// (with a one-line rationale per path). The student picks one before we
    /// commit to generating a full 20-week roadmap.
    /// </summary>
    Task<CareerSuggestionsResult> SuggestCareerPathsAsync(
        Student student,
        ChatSession session,
        CancellationToken ct = default);

    /// <summary>
    /// Generates the complete personalized roadmap. If <paramref name="chosenCareerTitle"/>
    /// is provided, Claude is constrained to building the plan for that specific
    /// career (the one the student picked from the 3 suggestions). If null, the
    /// engine picks the single best fit itself (legacy / fallback path).
    /// </summary>
    Task<GeneratedRoadmap> GenerateRoadmapAsync(
        Student student,
        ChatSession session,
        string? chosenCareerTitle = null,
        CancellationToken ct = default);

    /// <summary>
    /// Handles a post-roadmap conversational turn. The student has already
    /// completed assessment and received a PDF; this method answers thanks /
    /// follow-up questions without restarting the assessment. Returns
    /// ShouldRestart=true only if the student explicitly confirms they want a
    /// brand-new roadmap.
    /// </summary>
    Task<PostRoadmapTurnResult> PostRoadmapTurnAsync(
        Student student,
        GeneratedRoadmap roadmap,
        IReadOnlyList<ChatMessage> postRoadmapHistory,
        string latestUserMessage,
        CancellationToken ct = default);

    /// <summary>
    /// Generates the 10th-flow comprehensive guide. The student has answered
    /// 2-3 light questions (name, interest area, study/earn/both). Claude
    /// returns a full guide covering ALL options after 10th — streams,
    /// polytechnic, paramedical, earning paths — with the most relevant
    /// section ordered first based on the student's stated interest.
    /// </summary>
    Task<StudentGuide> GenerateTenthGuideAsync(
        Student student,
        ChatSession session,
        CancellationToken ct = default);

    /// <summary>
    /// Generates the 12th-flow comprehensive guide. Stream-aware: PCM/PCB/
    /// Commerce/Arts/BBA each get their own option set, with the student's
    /// stated direction ordered first. For PCB students paramedical options
    /// are always included; for PCM, polytechnic lateral entry is always
    /// included.
    /// </summary>
    Task<StudentGuide> GenerateTwelfthGuideAsync(
        Student student,
        ChatSession session,
        CancellationToken ct = default);
}
