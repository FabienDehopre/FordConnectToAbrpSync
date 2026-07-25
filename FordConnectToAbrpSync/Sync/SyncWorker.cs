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
/// Meaningful Change. The poll interval is read fresh every cycle, so config
/// changes take effect without a restart. Cycles never overlap; a cycle that
/// throws is logged and the loop survives to the next one.
/// </summary>
internal sealed class SyncWorker(
    FordTelemetryClient fordClient,
    AbrpClient abrpClient,
    SyncDecider decider,
    HeartbeatWriter heartbeat,
    IOptionsMonitor<SyncOptions> syncOptions,
    ILogger<SyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Ford → ABRP sync started.");
        TouchHeartbeatSafely();

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunCycleSafelyAsync(stoppingToken);
            TouchHeartbeatSafely();

            try
            {
                await Task.Delay(syncOptions.CurrentValue.Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    // The heartbeat records that the loop is still alive, not that the cycle
    // succeeded — a transient Ford/ABRP outage must not restart-loop the
    // container. A failure to write it (e.g. read-only mount) must likewise
    // never take the host down; BackgroundService's default exception
    // behavior stops the whole host on an unhandled exception here.
    private void TouchHeartbeatSafely()
    {
        try
        {
            heartbeat.Touch(DateTimeOffset.UtcNow, syncOptions.CurrentValue.Interval);
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
