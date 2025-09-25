using Microsoft.EntityFrameworkCore;
using WicsPlatform.Server.Data;

namespace WicsPlatform.Server.Services;

// Background service that scans schedules every 10 seconds and enqueues due ones
public class ScheduleScannerService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ScheduleScannerService> _logger;
    private readonly IScheduleExecutionService _executor;

    public ScheduleScannerService(IServiceProvider serviceProvider, ILogger<ScheduleScannerService> logger, IScheduleExecutionService executor)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _executor = executor;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ScheduleScannerService started.");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ScanOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while scanning schedules.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
            catch (TaskCanceledException) { }
        }
    }

    private async Task ScanOnceAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<wicsContext>();

        // current time in UTC (DB values are UTC)
        var now = DateTime.UtcNow;
        var startOfTodayUtc = now.Date; // UTC midnight

        // Find schedules that are not deleted and match today's weekday flag and time equal to current minute
        var candidates = await db.Schedules
            .Where(s => s.DeleteYn == "N")
            .Where(s => s.StartTime.Hour == now.Hour && s.StartTime.Minute == now.Minute)
            .Where(s =>
                (now.DayOfWeek == DayOfWeek.Monday && s.Monday == "Y") ||
                (now.DayOfWeek == DayOfWeek.Tuesday && s.Tuesday == "Y") ||
                (now.DayOfWeek == DayOfWeek.Wednesday && s.Wednesday == "Y") ||
                (now.DayOfWeek == DayOfWeek.Thursday && s.Thursday == "Y") ||
                (now.DayOfWeek == DayOfWeek.Friday && s.Friday == "Y") ||
                (now.DayOfWeek == DayOfWeek.Saturday && s.Saturday == "Y") ||
                (now.DayOfWeek == DayOfWeek.Sunday && s.Sunday == "Y")
            )
            // not executed today (UTC)
            .Where(s => s.LastExecuteAt == null || s.LastExecuteAt < startOfTodayUtc)
            .AsNoTracking()
            .ToListAsync(ct);

        if (candidates.Count > 0)
        {
            foreach (var s in candidates)
            {
                // 원자적으로 '오늘 실행됨' 표시(조건부 업데이트) 후 큐잉
                var affected = await db.Schedules
                    .Where(x => x.Id == s.Id && (x.LastExecuteAt == null || x.LastExecuteAt < startOfTodayUtc))
                    .ExecuteUpdateAsync(setters => setters.SetProperty(p => p.LastExecuteAt, now), ct);

                if (affected > 0)
                {
                    _logger.LogInformation("[ScheduleScanner] Enqueue schedule {ScheduleId} for execution at {Time}.", s.Id, now);
                    await _executor.EnqueueAsync(s.Id, ct);
                }
            }
        }
    }
}
