using InspectionTrack.Core.Data;
using InspectionTrack.Core.Models;
using InspectionTrack.Core.Rules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace InspectionTrack.Core.Services;

public record SweepResult(int NewlyOverdue, int TotalOverdue);

/// <summary>
/// Recalculates which active recommendations are past due. Runs daily from an
/// Azure Function timer, and can also be triggered from the API.
/// </summary>
public class OverdueSweep(AppDbContext db, TimeProvider clock, ILogger<OverdueSweep> logger)
{
    public async Task<SweepResult> RunAsync(CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

        var active = await db.Recommendations
            .Where(r => r.Status == RecommendationStatus.Open || r.Status == RecommendationStatus.InProgress)
            .ToListAsync(ct);

        var newlyOverdue = 0;
        foreach (var recommendation in active)
        {
            var overdue = RecommendationRules.IsOverdue(recommendation.Status, recommendation.DueOn, today);
            if (overdue && !recommendation.IsOverdue) newlyOverdue++;
            recommendation.IsOverdue = overdue;
        }

        await db.SaveChangesAsync(ct);

        var totalOverdue = active.Count(r => r.IsOverdue);
        logger.LogInformation(
            "Overdue sweep for {Today}: {NewlyOverdue} newly overdue, {TotalOverdue} overdue in total",
            today, newlyOverdue, totalOverdue);

        return new SweepResult(newlyOverdue, totalOverdue);
    }
}
