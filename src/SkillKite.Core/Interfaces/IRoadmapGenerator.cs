using SkillKite.Core.Dtos;
using SkillKite.Core.Models;

namespace SkillKite.Core.Interfaces;

/// <summary>
/// Generates a downloadable PDF for a completed roadmap and returns its public URL.
/// (The career roadmap structure itself is produced by <see cref="ICareerEngine"/>.)
/// </summary>
public interface IRoadmapGenerator
{
    Task<string> GenerateAsync(Student student, GeneratedRoadmap roadmap, CancellationToken ct = default);

    /// <summary>
    /// Renders the 10th/12th comprehensive guide PDF (3-6 pages). Shorter and
    /// flatter than the career roadmap PDF — no week-by-week breakdown, just
    /// sectioned option lists with consistent labelled blocks per option.
    /// </summary>
    Task<string> GenerateGuideAsync(Student student, StudentGuide guide, CancellationToken ct = default);
}
