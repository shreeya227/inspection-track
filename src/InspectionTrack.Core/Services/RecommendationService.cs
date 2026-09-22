using InspectionTrack.Core.Data;
using InspectionTrack.Core.Models;
using InspectionTrack.Core.Rules;
using Microsoft.EntityFrameworkCore;

namespace InspectionTrack.Core.Services;

public record TransitionResult(bool Succeeded, bool NotFound, string? Error)
{
    public static TransitionResult Ok() => new(true, false, null);
    public static TransitionResult Missing() => new(false, true, "Recommendation not found.");
    public static TransitionResult Rejected(string error) => new(false, false, error);
}

public class RecommendationService(AppDbContext db, TimeProvider clock)
{
    private DateOnly Today => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    /// <summary>Returns null if the site does not exist.</summary>
    public async Task<Recommendation?> CreateAsync(
        int siteId,
        string title,
        string description,
        Priority priority,
        DateOnly? dueOn,
        decimal? estimatedCost,
        CancellationToken ct = default)
    {
        if (!await db.Sites.AnyAsync(s => s.Id == siteId, ct))
            return null;

        var issuedOn = Today;
        var recommendation = new Recommendation
        {
            SiteId = siteId,
            Title = title.Trim(),
            Description = description.Trim(),
            Priority = priority,
            Status = RecommendationStatus.Open,
            IssuedOn = issuedOn,
            DueOn = dueOn ?? RecommendationRules.DefaultDueDate(priority, issuedOn),
            EstimatedCost = estimatedCost
        };

        db.Recommendations.Add(recommendation);
        await db.SaveChangesAsync(ct);
        return recommendation;
    }

    public async Task<TransitionResult> TransitionAsync(
        int recommendationId,
        RecommendationStatus to,
        string? note,
        string updatedBy,
        CancellationToken ct = default)
    {
        var recommendation = await db.Recommendations.FindAsync(new object[] { recommendationId }, ct);
        if (recommendation is null)
            return TransitionResult.Missing();

        if (recommendation.Status == to)
            return TransitionResult.Rejected($"Recommendation is already {to}.");

        if (!RecommendationRules.CanTransition(recommendation.Status, to))
            return TransitionResult.Rejected($"Cannot move a recommendation from {recommendation.Status} to {to}.");

        if (RecommendationRules.RequiresNote(to) && string.IsNullOrWhiteSpace(note))
            return TransitionResult.Rejected($"A note is required when marking a recommendation {to}.");

        db.StatusUpdates.Add(new StatusUpdate
        {
            RecommendationId = recommendation.Id,
            From = recommendation.Status,
            To = to,
            Note = note?.Trim() ?? string.Empty,
            UpdatedBy = updatedBy.Trim(),
            UpdatedAt = clock.GetUtcNow()
        });

        recommendation.Status = to;
        recommendation.CompletedOn = to == RecommendationStatus.Completed ? Today : null;
        recommendation.IsOverdue = RecommendationRules.IsOverdue(recommendation.Status, recommendation.DueOn, Today);

        await db.SaveChangesAsync(ct);
        return TransitionResult.Ok();
    }
}
