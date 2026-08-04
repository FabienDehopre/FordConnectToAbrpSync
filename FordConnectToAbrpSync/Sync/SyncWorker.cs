using FordConnectToAbrpSync.Abrp;
using FordConnectToAbrpSync.Configuration;
using FordConnectToAbrpSync.Ford;
using FordConnectToAbrpSync.Health;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FordConnectToAbrpSync.Sync;

/// <summary>
/// The Run loop. Each Sync Cycle: fetch a Snapshot, decide, and relay on a
/// Meaningful Change. The wait between cycles is the normal interval, or the
/// Idle Interval while the vehicle is Idle and no Boost Window is open; a
/// Wake/Sleep Signal cuts the wait short. Intervals are read fresh every
/// cycle, so config changes take effect without a restart. Cycles never
/// overlap; a cycle that throws is logged and the loop survives to the next
/// one.
/// </summary>
internal sealed class SyncWorker(
    FordTelemetryClient fordClient,
    AbrpClient abrpClient,
    SyncDecider decider,
    SyncPacer pacer,
    HeartbeatWriter heartbeat,
    IOptionsMonitor<SyncOptions> syncOptions,
    ILogger<SyncWorker> logger) : BackgroundService
{
    // Until a Snapshot says otherwise, assume the vehicle is active: starting
    // at the normal interval costs a few extra polls; starting Idle could
    // blind the first drive after a restart for a whole Idle Interval.
    private bool _isIdle;

    private TimeSpan _lastWait = TimeSpan.Zero;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Ford → ABRP sync started.");
        TouchHeartbeatSafely(syncOptions.CurrentValue.Interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunCycleSafelyAsync(stoppingToken);

            var wait = NextWait();
            TouchHeartbeatSafely(wait);

            try
            {
                var signalled = await pacer.WaitAsync(wait, stoppingToken);
                if (signalled)
                {
                    logger.LogInformation("External signal received; running a Sync Cycle now.");
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    // The Idle Interval only applies when the vehicle is Idle and no Boost
    // Window is open. A failed cycle keeps the last known state, so a Ford
    // outage while driving doesn't stretch the poll to the Idle Interval.
    private TimeSpan NextWait()
    {
        var options = syncOptions.CurrentValue;
        var idlePace = _isIdle && !pacer.IsBoosted(DateTimeOffset.UtcNow);
        var wait = idlePace ? options.IdleInterval : options.Interval;

        if (wait != _lastWait)
        {
            logger.LogInformation(
                idlePace
                    ? "Vehicle is Idle; polling every {Interval}."
                    : "Vehicle is active; polling every {Interval}.",
                wait);
            _lastWait = wait;
        }

        return wait;
    }

    // The heartbeat records that the loop is still alive, not that the cycle
    // succeeded — a transient Ford/ABRP outage must not restart-loop the
    // container. The deadline scales with the upcoming wait so an Idle-paced
    // loop isn't flagged unhealthy between polls. A failure to write it (e.g.
    // read-only mount) must likewise never take the host down;
    // BackgroundService's default exception behavior stops the whole host on
    // an unhandled exception here.
    private void TouchHeartbeatSafely(TimeSpan interval)
    {
        try
        {
            heartbeat.Touch(DateTimeOffset.UtcNow, interval);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to write healthcheck heartbeat.");
        }
    }

    private async Task RunCycleSafelyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RunCycleAsync(cancellationToken);
        }
        catch (FordTokenService.NotAuthenticatedException ex)
        {
            logger.LogError("{Message}", ex.Message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutting down — not an error.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Sync cycle failed; will retry next interval.");
        }
    }

    private async Task RunCycleAsync(CancellationToken cancellationToken)
    {
        var options = syncOptions.CurrentValue;

        var snapshot = await fordClient.GetTelemetryAsync(cancellationToken);
        if (snapshot is null)
        {
            logger.LogWarning("Ford returned an empty telemetry response; skipping.");
            return;
        }

        logger.LogDebug("Data received from Ford Connect: {@Data}", snapshot);
        _isIdle = IdleDetector.IsIdle(snapshot);

        var telemetry = AbrpTelemetryMapper.Map(snapshot, options.InvertPowerSign);
        var decision = decider.Decide(snapshot.UpdateTime, telemetry);

        switch (decision)
        {
            case SyncDecision.SkipStale:
                logger.LogDebug("Snapshot unchanged since last fetch (stale); skipping.");
                return;

            case SyncDecision.SkipNotWorthwhile:
                logger.LogDebug("Snapshot has neither charge nor position; skipping.");
                return;

            case SyncDecision.SkipNoChange:
                logger.LogDebug("No meaningful change since last relay; skipping.");
                return;

            case SyncDecision.Relay:
                await RelayAsync(telemetry, cancellationToken);
                return;
        }
    }

    private async Task RelayAsync(AbrpTelemetry telemetry, CancellationToken cancellationToken)
    {
        var acknowledged = await abrpClient.SendAsync(telemetry, cancellationToken);
        if (acknowledged)
        {
            decider.CommitRelay(telemetry);
            logger.LogInformation("Relayed telemetry to ABRP (utc={Utc}, soc={Soc}).",
                telemetry.Utc, telemetry.Soc);
            logger.LogDebug("Full Relayed telemetry to ABRP: {@Telemetry}", telemetry);
        }
        else
        {
            // Baseline not advanced — next cycle will retry the change.
            logger.LogWarning("ABRP did not acknowledge the telemetry; will retry next change.");
        }
    }
}
