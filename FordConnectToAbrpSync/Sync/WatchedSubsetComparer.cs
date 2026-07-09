namespace FordConnectToAbrpSync.Sync;

/// <summary>
/// Decides whether two Watched Subsets differ by a Meaningful Change, honoring
/// per-field noise tolerances.
/// </summary>
internal static class WatchedSubsetComparer
{
    public static bool HasMeaningfulChange(
        WatchedSubset baseline,
        WatchedSubset current,
        ChangeTolerances tolerances)
    {
        return NumericChanged(baseline.Soc, current.Soc, tolerances.SocPercent)
               || NumericChanged(baseline.Power, current.Power, tolerances.PowerKw)
               || NumericChanged(baseline.Speed, current.Speed, tolerances.SpeedKmh)
               || NumericChanged(baseline.Lat, current.Lat, tolerances.LatLonDegrees)
               || NumericChanged(baseline.Lon, current.Lon, tolerances.LatLonDegrees)
               || BoolChanged(baseline.IsCharging, current.IsCharging)
               || BoolChanged(baseline.IsDcfc, current.IsDcfc)
               || BoolChanged(baseline.IsParked, current.IsParked);
    }

    private static bool NumericChanged(double? baseline, double? current, double tolerance)
    {
        if (baseline is null && current is null)
        {
            return false;
        }

        if (baseline is null || current is null)
        {
            return true;
        }

        return Math.Abs(baseline.Value - current.Value) >= tolerance;
    }

    private static bool BoolChanged(bool? baseline, bool? current) => baseline != current;
}
