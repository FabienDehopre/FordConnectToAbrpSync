using FordConnectToAbrpSync.Ford;

namespace FordConnectToAbrpSync.Sync;

/// <summary>
/// Decides whether a Snapshot shows the vehicle Idle: ignition off and no
/// charge in progress. Anything ambiguous (missing metric, UNKNOWN, ACCESSORY)
/// counts as not Idle, so a misreported ignition can only ever keep the normal
/// poll interval — never starve an active vehicle of Sync Cycles.
/// </summary>
internal static class IdleDetector
{
    private const string IgnitionOff = "OFF";
    private const string ChargingInProgress = "IN_PROGRESS";

    public static bool IsIdle(FordTelemetryResponse snapshot)
    {
        var m = snapshot.Metrics;
        if (m is null)
        {
            return false;
        }

        var ignitionOff = string.Equals(
            m.IgnitionStatus?.Value, IgnitionOff, StringComparison.OrdinalIgnoreCase);
        var charging = string.Equals(
            m.XevBatteryChargeDisplayStatus?.Value, ChargingInProgress, StringComparison.OrdinalIgnoreCase);

        return ignitionOff && !charging;
    }
}
