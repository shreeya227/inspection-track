using InspectionTrack.Core.Models;

namespace InspectionTrack.Core.Rules;

/// <summary>
/// Business rules kept free of any database or HTTP code so they are
/// trivial to unit test and easy for a non-developer to read.
/// </summary>
public static class RecommendationRules
{
    public static int DaysToComplete(Priority priority) => priority switch
    {
        Priority.Critical => 30,
        Priority.High => 90,
        Priority.Medium => 180,
        Priority.Low => 365,
        _ => throw new ArgumentOutOfRangeException(nameof(priority))
    };

    public static DateOnly DefaultDueDate(Priority priority, DateOnly issuedOn) =>
        issuedOn.AddDays(DaysToComplete(priority));

    private static readonly Dictionary<RecommendationStatus, RecommendationStatus[]> AllowedTransitions = new()
    {
        [RecommendationStatus.Open] = [RecommendationStatus.InProgress, RecommendationStatus.Declined],
        [RecommendationStatus.InProgress] = [RecommendationStatus.Completed, RecommendationStatus.Open, RecommendationStatus.Declined],
        [RecommendationStatus.Completed] = [],
        // A client who declined can reconsider later.
        [RecommendationStatus.Declined] = [RecommendationStatus.Open],
    };

    public static bool CanTransition(RecommendationStatus from, RecommendationStatus to) =>
        AllowedTransitions[from].Contains(to);

    /// <summary>Completion needs evidence and declining needs a reason.</summary>
    public static bool RequiresNote(RecommendationStatus to) =>
        to is RecommendationStatus.Completed or RecommendationStatus.Declined;

    public static bool IsActive(RecommendationStatus status) =>
        status is RecommendationStatus.Open or RecommendationStatus.InProgress;

    public static bool IsOverdue(RecommendationStatus status, DateOnly dueOn, DateOnly today) =>
        IsActive(status) && today > dueOn;
}
