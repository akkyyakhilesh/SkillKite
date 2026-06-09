using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SkillKite.Core.Enums;
using SkillKite.Data;

namespace SkillKite.API.Controllers;

/// <summary>
/// Liveness/readiness probe + aggregate counts.
/// No auth — only returns counts, no PII. Safe to expose on a public URL.
/// </summary>
[ApiController]
public class HealthController : ControllerBase
{
    private readonly AppDbContext _db;
    public HealthController(AppDbContext db) => _db = db;

    /// <summary>
    /// Liveness + DB connectivity. Returns 200 if the API process is up AND
    /// it can issue a trivial SELECT against the database. Suitable for
    /// uptime monitors (UptimeRobot, Pingdom, Better Stack) and Azure
    /// App Service health probes.
    /// </summary>
    [HttpGet("/api/healthz")]
    public async Task<IActionResult> Health(CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await _db.Database.ExecuteSqlRawAsync("SELECT 1", ct);
            sw.Stop();
            return Ok(new
            {
                status = "ok",
                db = "connected",
                dbLatencyMs = sw.ElapsedMilliseconds,
                utc = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            sw.Stop();
            return StatusCode(503, new
            {
                status = "degraded",
                db = "error",
                error = ex.GetType().Name,
                utc = DateTime.UtcNow
            });
        }
    }

    /// <summary>
    /// Aggregate counts only — no PII, no names, no phones.
    /// Useful for: "X students helped" social-proof on the landing page,
    /// founder dashboards, weekly LinkedIn update numbers.
    /// </summary>
    [HttpGet("/api/stats")]
    public async Task<IActionResult> Stats(CancellationToken ct)
    {
        var students = await _db.Students.CountAsync(ct);
        var sessions = await _db.ChatSessions.CountAsync(ct);
        var completedAssessments = await _db.ChatSessions
            .CountAsync(s => s.Status == SessionStatus.Completed, ct);
        var roadmaps = await _db.Roadmaps.CountAsync(ct);
        var careersAvailable = await _db.CareerPaths.CountAsync(c => c.IsActive, ct);

        // Activity in the last 7 days — a low-pulse signal that the bot is being used,
        // not a vanity number. UTC because Postgres stores in UTC.
        var since = DateTime.UtcNow.AddDays(-7);
        var newStudentsLast7d = await _db.Students.CountAsync(s => s.CreatedAt >= since, ct);
        var roadmapsLast7d = await _db.Roadmaps.CountAsync(r => r.CreatedAt >= since, ct);

        // Feedback ratings per flow. We don't have a Feedback table —
        // rating is stored as a string key in ChatSession.AssessmentDataJson
        // ("feedbackRating" = "Useful"/"Ok"/"NotUseful"/"Skipped"). Pull the
        // raw payload + flowType for any session that has a rating, then
        // bucket in memory (small N for the foreseeable future; if it grows
        // past tens of thousands we'll move to a generated column).
        // Pull JSON for all completed sessions; filter for those carrying a
        // feedbackRating in memory. EF Core can't translate Contains() on a
        // jsonb column to SQL LIKE (jsonb doesn't support it), and we don't
        // want a Postgres-specific raw query in a portable controller. At
        // tens of thousands of sessions this is still cheap.
        var allCompletedData = await _db.ChatSessions
            .Where(s => s.Status == SessionStatus.Completed)
            .Select(s => s.AssessmentDataJson)
            .ToListAsync(ct);

        var feedback = new Dictionary<string, Dictionary<string, int>>();
        foreach (var json in allCompletedData)
        {
            if (string.IsNullOrWhiteSpace(json) || json == "{}") continue;
            string flow = "unknown", rating = "Skipped";
            bool hasRating = false;
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) continue;
                if (doc.RootElement.TryGetProperty("flowType", out var f) && f.ValueKind == JsonValueKind.String)
                    flow = f.GetString() ?? "unknown";
                if (doc.RootElement.TryGetProperty("feedbackRating", out var r) && r.ValueKind == JsonValueKind.String)
                {
                    rating = r.GetString() ?? "Skipped";
                    hasRating = true;
                }
            }
            catch { continue; }

            if (!hasRating) continue;

            if (!feedback.TryGetValue(flow, out var bucket))
            {
                bucket = new Dictionary<string, int>
                {
                    ["Useful"] = 0, ["Ok"] = 0, ["NotUseful"] = 0, ["Skipped"] = 0
                };
                feedback[flow] = bucket;
            }
            if (bucket.ContainsKey(rating)) bucket[rating]++;
        }

        return Ok(new
        {
            totals = new
            {
                students,
                sessions,
                completedAssessments,
                roadmapsGenerated = roadmaps,
                careersAvailable
            },
            last7d = new
            {
                newStudents = newStudentsLast7d,
                roadmaps = roadmapsLast7d
            },
            feedback,  // { "career": {Useful, Ok, NotUseful, Skipped}, "10th": ..., ... }
            utc = DateTime.UtcNow
        });
    }
}
