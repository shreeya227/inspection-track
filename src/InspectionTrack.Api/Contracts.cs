using InspectionTrack.Core.Models;

namespace InspectionTrack.Api;

// Requests
public record CreateSiteRequest(string Name, string ClientName, string Address);

public record CreateRecommendationRequest(
    string Title,
    string Description,
    Priority Priority,
    DateOnly? DueOn,
    decimal? EstimatedCost);

public record TransitionRequest(RecommendationStatus Status, string? Note, string UpdatedBy);

// Responses: explicit shapes so we never serialise entity graphs or leak internal fields.
public record SiteSummary(int Id, string Name, string ClientName, string Address, int ActiveCount, int OverdueCount);

public record RecommendationDto(
    int Id,
    string Title,
    string Description,
    Priority Priority,
    RecommendationStatus Status,
    DateOnly IssuedOn,
    DateOnly DueOn,
    DateOnly? CompletedOn,
    bool IsOverdue,
    decimal? EstimatedCost);

public record StatusUpdateDto(RecommendationStatus From, RecommendationStatus To, string Note, string UpdatedBy, DateTimeOffset UpdatedAt);

public record OverdueItem(int Id, string Title, string SiteName, Priority Priority, DateOnly DueOn, int DaysOverdue);

public record DashboardDto(
    int ActiveCount,
    int OverdueCount,
    Dictionary<string, int> ActiveByPriority,
    decimal ActiveEstimatedCost,
    List<OverdueItem> MostOverdue);

public static class Validation
{
    public static Dictionary<string, string[]> Errors() => new();

    public static void Require(this Dictionary<string, string[]> errors, bool condition, string field, string message)
    {
        if (!condition) errors[field] = [message];
    }
}
