using InspectionTrack.Core.Data;
using InspectionTrack.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {
        // Same data setup as the API, so both read and write one database.
        services.AddInspectionTrackData(context.Configuration);
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<OverdueSweep>();
    })
    .Build();

host.Run();
