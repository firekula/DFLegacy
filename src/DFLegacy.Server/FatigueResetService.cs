namespace DFLegacy.Server;

public sealed class FatigueResetService(
    JsonGameStore store,
    CharacterSessionRegistry characterSessions,
    ILogger<FatigueResetService> logger) : BackgroundService
{
    internal static readonly TimeSpan BeijingOffset = TimeSpan.FromHours(8);
    internal static readonly TimeSpan ResetTime = TimeSpan.FromHours(6);
    private static readonly TimeSpan MaximumClockCheckDelay = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await ResetCurrentDayAsync(DateTimeOffset.UtcNow, stoppingToken);
            await Task.Delay(GetNextCheckDelay(DateTimeOffset.UtcNow), stoppingToken);
        }
    }

    internal async Task ResetCurrentDayAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var result = await store.ResetFatigueForCurrentDayAsync(now, cancellationToken);
        if (!result.NewFatigueDay)
            return;

        // The capacity can change at reset even for characters with zero usage.
        characterSessions.NotifyDailyFatigueReset();
        logger.LogInformation(
            "Daily 06:00 fatigue reset completed for day {FatigueDayKey}; reset {CharacterCount} character(s).",
            result.FatigueDayKey,
            result.ResetCharacterCount);
    }

    private static TimeSpan GetNextCheckDelay(DateTimeOffset now)
    {
        now = now.ToOffset(BeijingOffset);
        var nextResetDate = now.TimeOfDay < ResetTime
            ? now.Date
            : now.Date.AddDays(1);
        var nextReset = new DateTimeOffset(nextResetDate + ResetTime, BeijingOffset);
        var delay = nextReset - now;
        if (delay <= TimeSpan.Zero)
        {
            return TimeSpan.FromSeconds(1);
        }

        return delay < MaximumClockCheckDelay
            ? delay
            : MaximumClockCheckDelay;
    }
}
