using InspectionTrack.Core.Data;
using InspectionTrack.Core.Models;
using InspectionTrack.Core.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace InspectionTrack.Tests;

/// <summary>
/// Exercises the service against a real in-memory SQLite database, and a fake
/// clock so tests can move time forward to make recommendations overdue.
/// </summary>
public class RecommendationWorkflowTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero));
    private readonly int _siteId;

    public RecommendationWorkflowTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;

        using var db = new AppDbContext(_options);
        db.Database.EnsureCreated();
        var site = new Site { Name = "Test Warehouse", ClientName = "Test Client" };
        db.Sites.Add(site);
        db.SaveChanges();
        _siteId = site.Id;
    }

    public void Dispose() => _connection.Dispose();

    private AppDbContext Db() => new(_options);

    private async Task<Recommendation> Create(Priority priority, DateOnly? dueOn = null)
    {
        await using var db = Db();
        var created = await new RecommendationService(db, _clock)
            .CreateAsync(_siteId, "Install sprinklers", "Rack storage unprotected", priority, dueOn, 50000m);
        return created!;
    }

    private async Task<TransitionResult> Move(int id, RecommendationStatus to, string? note = null)
    {
        await using var db = Db();
        return await new RecommendationService(db, _clock).TransitionAsync(id, to, note, "Test User");
    }

    private async Task<Recommendation> Reload(int id)
    {
        await using var db = Db();
        return (await db.Recommendations.FindAsync(id))!;
    }

    private async Task<SweepResult> Sweep()
    {
        await using var db = Db();
        return await new OverdueSweep(db, _clock, NullLogger<OverdueSweep>.Instance).RunAsync();
    }

    [Fact]
    public async Task New_recommendation_gets_a_due_date_from_its_priority()
    {
        var rec = await Create(Priority.Critical);

        Assert.Equal(new DateOnly(2026, 1, 1), rec.IssuedOn);
        Assert.Equal(new DateOnly(2026, 1, 31), rec.DueOn);
        Assert.Equal(RecommendationStatus.Open, rec.Status);
    }

    [Fact]
    public async Task Explicit_due_date_overrides_the_default()
    {
        var rec = await Create(Priority.Low, new DateOnly(2026, 3, 1));
        Assert.Equal(new DateOnly(2026, 3, 1), rec.DueOn);
    }

    [Fact]
    public async Task Creating_for_a_missing_site_returns_null()
    {
        await using var db = Db();
        var result = await new RecommendationService(db, _clock)
            .CreateAsync(9999, "Title", "", Priority.High, null, null);
        Assert.Null(result);
    }

    [Fact]
    public async Task Valid_transition_updates_status_and_writes_an_audit_entry()
    {
        var rec = await Create(Priority.High);

        var result = await Move(rec.Id, RecommendationStatus.InProgress);

        Assert.True(result.Succeeded);
        Assert.Equal(RecommendationStatus.InProgress, (await Reload(rec.Id)).Status);

        await using var db = Db();
        var audit = await db.StatusUpdates.SingleAsync(u => u.RecommendationId == rec.Id);
        Assert.Equal(RecommendationStatus.Open, audit.From);
        Assert.Equal(RecommendationStatus.InProgress, audit.To);
        Assert.Equal("Test User", audit.UpdatedBy);
    }

    [Fact]
    public async Task Cannot_complete_without_starting_work()
    {
        var rec = await Create(Priority.High);

        var result = await Move(rec.Id, RecommendationStatus.Completed, "done");

        Assert.False(result.Succeeded);
        Assert.Equal(RecommendationStatus.Open, (await Reload(rec.Id)).Status);
    }

    [Fact]
    public async Task Completing_without_evidence_is_rejected()
    {
        var rec = await Create(Priority.High);
        await Move(rec.Id, RecommendationStatus.InProgress);

        var result = await Move(rec.Id, RecommendationStatus.Completed, note: "   ");

        Assert.False(result.Succeeded);
        Assert.Contains("note is required", result.Error);
    }

    [Fact]
    public async Task Unknown_recommendation_reports_not_found()
    {
        var result = await Move(9999, RecommendationStatus.InProgress);
        Assert.True(result.NotFound);
    }

    [Fact]
    public async Task Sweep_flags_recommendations_once_they_pass_their_due_date()
    {
        var rec = await Create(Priority.Critical); // due Jan 31

        Assert.Equal(0, (await Sweep()).TotalOverdue);

        _clock.Advance(TimeSpan.FromDays(31)); // now Feb 1
        var result = await Sweep();

        Assert.Equal(1, result.NewlyOverdue);
        Assert.Equal(1, result.TotalOverdue);
        Assert.True((await Reload(rec.Id)).IsOverdue);
    }

    [Fact]
    public async Task Running_the_sweep_twice_does_not_double_count()
    {
        await Create(Priority.Critical);
        _clock.Advance(TimeSpan.FromDays(40));

        await Sweep();
        var second = await Sweep();

        Assert.Equal(0, second.NewlyOverdue);
        Assert.Equal(1, second.TotalOverdue);
    }

    [Fact]
    public async Task Completing_an_overdue_recommendation_clears_the_flag()
    {
        var rec = await Create(Priority.Critical);
        _clock.Advance(TimeSpan.FromDays(40));
        await Sweep();
        await Move(rec.Id, RecommendationStatus.InProgress);

        await Move(rec.Id, RecommendationStatus.Completed, "Contractor sign-off attached");

        var reloaded = await Reload(rec.Id);
        Assert.False(reloaded.IsOverdue);
        Assert.Equal(DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime), reloaded.CompletedOn);
    }
}
