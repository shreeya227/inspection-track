using InspectionTrack.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace InspectionTrack.Core.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Site> Sites => Set<Site>();
    public DbSet<Recommendation> Recommendations => Set<Recommendation>();
    public DbSet<StatusUpdate> StatusUpdates => Set<StatusUpdate>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Site>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.ClientName).HasMaxLength(200);
            e.Property(x => x.Address).HasMaxLength(400);
        });

        modelBuilder.Entity<Recommendation>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(200).IsRequired();
            e.Property(x => x.Description).HasMaxLength(4000);
            e.Property(x => x.Priority).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.EstimatedCost).HasPrecision(12, 2);
            e.HasIndex(x => new { x.Status, x.DueOn });
            e.HasOne(x => x.Site)
                .WithMany(s => s.Recommendations)
                .HasForeignKey(x => x.SiteId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<StatusUpdate>(e =>
        {
            e.Property(x => x.From).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.To).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Note).HasMaxLength(2000);
            e.Property(x => x.UpdatedBy).HasMaxLength(200);
            e.HasOne<Recommendation>()
                .WithMany(r => r.Updates)
                .HasForeignKey(x => x.RecommendationId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
