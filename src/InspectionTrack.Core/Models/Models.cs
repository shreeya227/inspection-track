namespace InspectionTrack.Core.Models;

public enum Priority { Critical, High, Medium, Low }

public enum RecommendationStatus { Open, InProgress, Completed, Declined }

/// <summary>A client property that has been inspected.</summary>
public class Site
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ClientName { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public List<Recommendation> Recommendations { get; set; } = new();
}

/// <summary>
/// A loss-prevention recommendation issued after an inspection,
/// e.g. "Install automatic sprinklers in rack storage area".
/// </summary>
public class Recommendation
{
    public int Id { get; set; }
    public int SiteId { get; set; }
    public Site? Site { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Priority Priority { get; set; }
    public RecommendationStatus Status { get; set; } = RecommendationStatus.Open;
    public DateOnly IssuedOn { get; set; }
    public DateOnly DueOn { get; set; }
    public DateOnly? CompletedOn { get; set; }
    public bool IsOverdue { get; set; }
    public decimal? EstimatedCost { get; set; }
    public List<StatusUpdate> Updates { get; set; } = new();
}

/// <summary>
/// Audit trail entry. Every status change is recorded with who made it and why,
/// so an insurer or consultant can see how a recommendation reached its current state.
/// </summary>
public class StatusUpdate
{
    public int Id { get; set; }
    public int RecommendationId { get; set; }
    public RecommendationStatus From { get; set; }
    public RecommendationStatus To { get; set; }
    public string Note { get; set; } = string.Empty;
    public string UpdatedBy { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
}
