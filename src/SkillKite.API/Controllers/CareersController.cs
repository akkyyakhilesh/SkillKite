using Microsoft.AspNetCore.Mvc;
using SkillKite.Core.Enums;
using SkillKite.Core.Interfaces;

namespace SkillKite.API.Controllers;

[ApiController]
[Route("api/careers")]
public class CareersController : ControllerBase
{
    private readonly ICareerPathRepository _repo;
    public CareersController(ICareerPathRepository repo) => _repo = repo;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? category, CancellationToken ct)
    {
        CareerCategory? cat = null;
        if (!string.IsNullOrWhiteSpace(category) &&
            Enum.TryParse<CareerCategory>(category, ignoreCase: true, out var parsed))
        {
            cat = parsed;
        }

        var items = (await _repo.ListAsync(cat, ct)).Select(c => new
        {
            c.Id,
            c.Title,
            c.TitleHi,
            Category = c.Category.ToString(),
            c.SalaryRangeMin,
            c.SalaryRangeMax,
            c.RemoteFriendly,
            DemandLevel = c.DemandLevel.ToString(),
            c.TimeToJobReady
        });

        return Ok(items);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var career = await _repo.GetAsync(id, ct);
        return career is null ? NotFound() : Ok(career);
    }

    [HttpGet("{id:guid}/resources")]
    public async Task<IActionResult> Resources(Guid id, CancellationToken ct)
    {
        var resources = await _repo.GetResourcesAsync(id, ct);
        return Ok(resources);
    }
}
