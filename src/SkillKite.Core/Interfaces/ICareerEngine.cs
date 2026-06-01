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
    /// Generates a complete personalized roadmap once assessment is complete.
    /// </summary>
    Task<GeneratedRoadmap> GenerateRoadmapAsync(
        Student student,
        ChatSession session,
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
}
