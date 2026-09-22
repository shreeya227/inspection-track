using InspectionTrack.Core.Data;
using InspectionTrack.Core.Models;
using InspectionTrack.Core.Rules;

namespace InspectionTrack.Api;

/// <summary>Realistic demo data for local development only. Fictional sites and clients.</summary>
public static class SeedData
{
    public static async Task EnsureSeededAsync(AppDbContext db, TimeProvider clock)
    {
        if (db.Sites.Any()) return;

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

        Recommendation Rec(string title, string description, Priority priority, int issuedDaysAgo,
            RecommendationStatus status = RecommendationStatus.Open, decimal? cost = null)
        {
            var issued = today.AddDays(-issuedDaysAgo);
            var due = RecommendationRules.DefaultDueDate(priority, issued);
            return new Recommendation
            {
                Title = title,
                Description = description,
                Priority = priority,
                Status = status,
                IssuedOn = issued,
                DueOn = due,
                IsOverdue = RecommendationRules.IsOverdue(status, due, today),
                EstimatedCost = cost
            };
        }

        db.Sites.AddRange(
            new Site
            {
                Name = "Riverside Distribution Center",
                ClientName = "Northwind Logistics",
                Address = "400 Harbor Rd, Newark, NJ",
                Recommendations =
                {
                    Rec("Install in-rack sprinklers in high-bay storage",
                        "Rack storage exceeds 20 ft with ceiling-only protection. Add in-rack sprinklers per NFPA 13.",
                        Priority.Critical, 45, cost: 180000m),
                    Rec("Clear obstructions below sprinkler heads in aisle 4",
                        "Stock stacked within 18 inches of deflectors, blocking discharge pattern.",
                        Priority.High, 20, RecommendationStatus.InProgress, 0m),
                    Rec("Implement a formal hot work permit program",
                        "Welding performed in the maintenance bay without permits or fire watch.",
                        Priority.High, 120),
                }
            },
            new Site
            {
                Name = "Northgate Manufacturing Plant",
                ClientName = "Contoso Industrial",
                Address = "1200 Industrial Pkwy, Woodbridge, NJ",
                Recommendations =
                {
                    Rec("Replace deteriorated fire pump controller",
                        "Controller shows corrosion and failed the weekly churn test twice.",
                        Priority.Critical, 10, cost: 42000m),
                    Rec("Provide secondary containment for flammable liquid storage",
                        "Drums stored on bare concrete with no spill containment.",
                        Priority.Medium, 200, cost: 15000m),
                    Rec("Add backflow preventer testing to the annual schedule",
                        "No record of backflow preventer testing in the last three years.",
                        Priority.Low, 60),
                }
            },
            new Site
            {
                Name = "Harbor Cold Storage",
                ClientName = "Fabrikam Foods",
                Address = "88 Pier St, Elizabeth, NJ",
                Recommendations =
                {
                    Rec("Update emergency response plan and run a drill",
                        "Plan last revised in 2019 and references staff no longer employed.",
                        Priority.Medium, 90, RecommendationStatus.InProgress),
                    Rec("Install lightning protection on the roof-mounted refrigeration units",
                        "Exposed rooftop equipment with no lightning protection system.",
                        Priority.Low, 400, cost: 26000m),
                }
            });

        await db.SaveChangesAsync();
    }
}
