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
}
