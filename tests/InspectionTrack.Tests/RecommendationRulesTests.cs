using InspectionTrack.Core.Models;
using InspectionTrack.Core.Rules;
using Xunit;

namespace InspectionTrack.Tests;

public class RecommendationRulesTests
{
    private static readonly DateOnly Jan1 = new(2026, 1, 1);

    [Theory]
    [InlineData(Priority.Critical, 30)]
    [InlineData(Priority.High, 90)]
    [InlineData(Priority.Medium, 180)]
    [InlineData(Priority.Low, 365)]
    public void Due_date_follows_priority(Priority priority, int days)
    {
        Assert.Equal(Jan1.AddDays(days), RecommendationRules.DefaultDueDate(priority, Jan1));
    }

    [Theory]
    [InlineData(RecommendationStatus.Open, RecommendationStatus.InProgress)]
    [InlineData(RecommendationStatus.Open, RecommendationStatus.Declined)]
    [InlineData(RecommendationStatus.InProgress, RecommendationStatus.Completed)]
    [InlineData(RecommendationStatus.InProgress, RecommendationStatus.Open)]
    [InlineData(RecommendationStatus.Declined, RecommendationStatus.Open)]
    public void Allowed_transitions_are_permitted(RecommendationStatus from, RecommendationStatus to)
    {
        Assert.True(RecommendationRules.CanTransition(from, to));
    }

    [Theory]
    [InlineData(RecommendationStatus.Open, RecommendationStatus.Completed)]   // must start work first
    [InlineData(RecommendationStatus.Completed, RecommendationStatus.Open)]   // completion is final
    [InlineData(RecommendationStatus.Completed, RecommendationStatus.InProgress)]
    [InlineData(RecommendationStatus.Declined, RecommendationStatus.Completed)]
    public void Disallowed_transitions_are_refused(RecommendationStatus from, RecommendationStatus to)
    {
        Assert.False(RecommendationRules.CanTransition(from, to));
    }

    [Theory]
    [InlineData(RecommendationStatus.Completed, true)]
    [InlineData(RecommendationStatus.Declined, true)]
    [InlineData(RecommendationStatus.InProgress, false)]
    [InlineData(RecommendationStatus.Open, false)]
    public void Completion_and_declining_require_a_note(RecommendationStatus to, bool required)
    {
        Assert.Equal(required, RecommendationRules.RequiresNote(to));
    }

    [Fact]
    public void Active_recommendation_past_its_due_date_is_overdue()
    {
        Assert.True(RecommendationRules.IsOverdue(RecommendationStatus.Open, Jan1, Jan1.AddDays(1)));
        Assert.True(RecommendationRules.IsOverdue(RecommendationStatus.InProgress, Jan1, Jan1.AddDays(1)));
    }

    [Fact]
    public void Recommendation_due_today_is_not_yet_overdue()
    {
        Assert.False(RecommendationRules.IsOverdue(RecommendationStatus.Open, Jan1, Jan1));
    }

    [Theory]
    [InlineData(RecommendationStatus.Completed)]
    [InlineData(RecommendationStatus.Declined)]
    public void Closed_recommendation_is_never_overdue(RecommendationStatus status)
    {
        Assert.False(RecommendationRules.IsOverdue(status, Jan1, Jan1.AddDays(500)));
    }
}
