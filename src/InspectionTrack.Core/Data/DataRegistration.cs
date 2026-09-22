using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace InspectionTrack.Core.Data;

public static class DataRegistration
{
    /// <summary>
    /// SQLite for local development, Azure SQL in the cloud. Chosen by configuration
    /// so the same code runs in both places and the API and Function share one setup.
    /// </summary>
    public static IServiceCollection AddInspectionTrackData(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default") ?? "Data Source=inspectiontrack.db";
        var provider = configuration["Database:Provider"] ?? "Sqlite";

        services.AddDbContext<AppDbContext>(options =>
        {
            if (provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
                options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure());
            else
                options.UseSqlite(connectionString);
        });

        return services;
    }
}
