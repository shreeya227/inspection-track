using System.Text.Json.Serialization;
using InspectionTrack.Api;
using InspectionTrack.Core.Data;
using InspectionTrack.Core.Models;
using InspectionTrack.Core.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInspectionTrackData(builder.Configuration);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<RecommendationService>();
builder.Services.AddScoped<OverdueSweep>();

builder.Services.AddApplicationInsightsTelemetry();
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
    if (app.Environment.IsDevelopment())
        await SeedData.EnsureSeededAsync(db, TimeProvider.System);
}

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapHealthChecks("/health");

var api = app.MapGroup("/api");

// ---------------- Sites ----------------

api.MapGet("/sites", async (AppDbContext db, CancellationToken ct) =>
    await db.Sites
        .OrderBy(s => s.Name)
        .Select(s => new SiteSummary(
            s.Id, s.Name, s.ClientName, s.Address,
            s.Recommendations.Count(r => r.Status == RecommendationStatus.Open || r.Status == RecommendationStatus.InProgress),
            s.Recommendations.Count(r => r.IsOverdue)))
        .ToListAsync(ct));

api.MapPost("/sites", async (CreateSiteRequest request, AppDbContext db, CancellationToken ct) =>
{
    var errors = Validation.Errors();
    errors.Require(!string.IsNullOrWhiteSpace(request.Name), "name", "Site name is required.");
    errors.Require(!string.IsNullOrWhiteSpace(request.ClientName), "clientName", "Client name is required.");
    if (errors.Count > 0) return Results.ValidationProblem(errors);

    var site = new Site
    {
        Name = request.Name.Trim(),
        ClientName = request.ClientName.Trim(),
        Address = request.Address?.Trim() ?? string.Empty
    };
    db.Sites.Add(site);
    await db.SaveChangesAsync(ct);

    return Results.Created($"/api/sites/{site.Id}", new SiteSummary(site.Id, site.Name, site.ClientName, site.Address, 0, 0));
});

// ---------------- Recommendations ----------------

api.MapGet("/sites/{siteId:int}/recommendations", async (int siteId, AppDbContext db, CancellationToken ct) =>
{
    if (!await db.Sites.AnyAsync(s => s.Id == siteId, ct)) return Results.NotFound();

    var items = await db.Recommendations
        .Where(r => r.SiteId == siteId)
        .Select(r => new RecommendationDto(
            r.Id, r.Title, r.Description, r.Priority, r.Status,
            r.IssuedOn, r.DueOn, r.CompletedOn, r.IsOverdue, r.EstimatedCost))
        .ToListAsync(ct);

    // Sorted in memory: overdue first, then by severity, then soonest due.
    var sorted = items
        .OrderByDescending(r => r.IsOverdue)
        .ThenBy(r => r.Priority)
        .ThenBy(r => r.DueOn)
        .ToList();

    return Results.Ok(sorted);
});

api.MapPost("/sites/{siteId:int}/recommendations", async (
    int siteId, CreateRecommendationRequest request, RecommendationService service,
    TimeProvider clock, CancellationToken ct) =>
{
    var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
    var errors = Validation.Errors();
    errors.Require(!string.IsNullOrWhiteSpace(request.Title), "title", "Title is required.");
    errors.Require(request.DueOn is null || request.DueOn >= today, "dueOn", "Due date cannot be in the past.");
    errors.Require(request.EstimatedCost is null or >= 0m, "estimatedCost", "Estimated cost cannot be negative.");
    if (errors.Count > 0) return Results.ValidationProblem(errors);

    var created = await service.CreateAsync(
        siteId, request.Title, request.Description ?? string.Empty,
        request.Priority, request.DueOn, request.EstimatedCost, ct);

    return created is null
        ? Results.NotFound()
        : Results.Created($"/api/recommendations/{created.Id}", new RecommendationDto(
            created.Id, created.Title, created.Description, created.Priority, created.Status,
            created.IssuedOn, created.DueOn, created.CompletedOn, created.IsOverdue, created.EstimatedCost));
});

api.MapPost("/recommendations/{id:int}/status", async (
    int id, TransitionRequest request, RecommendationService service, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.UpdatedBy))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["updatedBy"] = ["Who is making this change is required."] });

    var result = await service.TransitionAsync(id, request.Status, request.Note, request.UpdatedBy, ct);

    if (result.NotFound) return Results.NotFound();
    if (!result.Succeeded) return Results.BadRequest(new { error = result.Error });
    return Results.NoContent();
});

api.MapGet("/recommendations/{id:int}/history", async (int id, AppDbContext db, CancellationToken ct) =>
{
    if (!await db.Recommendations.AnyAsync(r => r.Id == id, ct)) return Results.NotFound();

    var history = await db.StatusUpdates
        .Where(u => u.RecommendationId == id)
        .OrderBy(u => u.Id)
        .Select(u => new StatusUpdateDto(u.From, u.To, u.Note, u.UpdatedBy, u.UpdatedAt))
        .ToListAsync(ct);

    return Results.Ok(history);
});

// ---------------- Dashboard & operations ----------------

api.MapGet("/dashboard", async (AppDbContext db, TimeProvider clock, CancellationToken ct) =>
{
    var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    var active = await db.Recommendations
        .Where(r => r.Status == RecommendationStatus.Open || r.Status == RecommendationStatus.InProgress)
        .Select(r => new { r.Id, r.Title, r.Priority, r.DueOn, r.IsOverdue, r.EstimatedCost, SiteName = r.Site!.Name })
        .ToListAsync(ct);

    return new DashboardDto(
        ActiveCount: active.Count,
        OverdueCount: active.Count(r => r.IsOverdue),
        ActiveByPriority: Enum.GetValues<Priority>().ToDictionary(p => p.ToString(), p => active.Count(r => r.Priority == p)),
        ActiveEstimatedCost: active.Sum(r => r.EstimatedCost ?? 0m),
        MostOverdue: active
            .Where(r => r.IsOverdue)
            .OrderBy(r => r.DueOn)
            .Take(5)
            .Select(r => new OverdueItem(r.Id, r.Title, r.SiteName, r.Priority, r.DueOn, today.DayNumber - r.DueOn.DayNumber))
            .ToList());
});

// Normally run by the Azure Function timer; exposed here for local testing and manual runs.
api.MapPost("/admin/sweep", async (OverdueSweep sweep, CancellationToken ct) =>
    Results.Ok(await sweep.RunAsync(ct)));

app.Run();

public partial class Program { }
