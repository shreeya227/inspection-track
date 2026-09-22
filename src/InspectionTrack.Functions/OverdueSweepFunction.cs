using InspectionTrack.Core.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace InspectionTrack.Functions;

public class OverdueSweepFunction(OverdueSweep sweep, ILogger<OverdueSweepFunction> logger)
{
    /// <summary>Runs every day at 07:00 UTC and flags recommendations that have passed their due date.</summary>
    [Function("OverdueSweep")]
    public async Task Run([TimerTrigger("0 0 7 * * *")] TimerInfo timer, CancellationToken ct)
    {
        var result = await sweep.RunAsync(ct);

        logger.LogInformation(
            "Overdue sweep complete: {NewlyOverdue} newly overdue, {TotalOverdue} total. Next run: {Next}",
            result.NewlyOverdue, result.TotalOverdue, timer.ScheduleStatus?.Next);
    }
}
