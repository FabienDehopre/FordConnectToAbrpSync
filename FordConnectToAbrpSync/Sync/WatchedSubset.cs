using FordConnectToAbrpSync.Abrp;

namespace FordConnectToAbrpSync.Sync;

/// <summary>
/// The route-relevant values whose change makes a Telemetry Snapshot worth
/// relaying. Everything outside this set is still sent in the payload but never
/// triggers a Relay.
/// </summary>
internal readonly record struct WatchedSubset(
    double? Soc,
    double? Power,
    double? Speed,
    double? Lat,
    double? Lon,
    bool? IsCharging,
    bool? IsDcfc,
    bool? IsParked)
{
    public static WatchedSubset From(AbrpTelemetry tlm) => new(
        tlm.Soc,
        tlm.Power,
        tlm.Speed,
        tlm.Lat,
        tlm.Lon,
        tlm.IsCharging,
        tlm.IsDcfc,
        tlm.IsParked);
}
