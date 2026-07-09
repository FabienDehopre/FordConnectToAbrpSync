using FordConnectToAbrpSync.Abrp;

namespace FordConnectToAbrpSync.Sync;

internal enum SyncDecision
{
    /// <summary>Ford report time has not advanced — no new data.</summary>
    SkipStale,

    /// <summary>Nothing useful to relay (neither charge nor position present).</summary>
    SkipNotWorthwhile,

    /// <summary>Watched Subset unchanged within tolerances since the last Relay.</summary>
    SkipNoChange,

    /// <summary>A Meaningful Change occurred — relay this Snapshot.</summary>
    Relay,
}

/// <summary>
/// Holds the in-memory Baseline and last-seen report time, and decides whether a
/// mapped Snapshot warrants a Relay. Not thread-safe: the Sync loop is sequential.
/// </summary>
internal sealed class SyncDecider(ChangeTolerances tolerances)
{
    private DateTimeOffset? _lastUpdateTime;
    private WatchedSubset? _baseline;

    public SyncDecision Decide(DateTimeOffset? updateTime, AbrpTelemetry tlm)
    {
        if (IsStale(updateTime))
        {
            return SyncDecision.SkipStale;
        }

        if (!IsWorthRelaying(tlm))
        {
            return SyncDecision.SkipNotWorthwhile;
        }

        var current = WatchedSubset.From(tlm);
        if (_baseline is { } baseline
            && !WatchedSubsetComparer.HasMeaningfulChange(baseline, current, tolerances))
        {
            return SyncDecision.SkipNoChange;
        }

        return SyncDecision.Relay;
    }

    /// <summary>Advance the Baseline after a successful Relay.</summary>
    public void CommitRelay(AbrpTelemetry relayed) => _baseline = WatchedSubset.From(relayed);

    /// <summary>A Snapshot is worth relaying if it carries charge or a position fix.</summary>
    internal static bool IsWorthRelaying(AbrpTelemetry tlm) =>
        tlm.Soc.HasValue || (tlm.Lat.HasValue && tlm.Lon.HasValue);

    private bool IsStale(DateTimeOffset? updateTime)
    {
        if (updateTime is null)
        {
            return false;
        }

        var stale = _lastUpdateTime == updateTime;
        _lastUpdateTime = updateTime;
        return stale;
    }
}
