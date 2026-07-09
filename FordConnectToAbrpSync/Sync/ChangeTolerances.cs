namespace FordConnectToAbrpSync.Sync;

/// <summary>
/// Noise floors below which a Watched Subset difference is not a Meaningful
/// Change. Guards against GPS jitter and sensor drift spamming ABRP while parked.
/// </summary>
internal sealed class ChangeTolerances
{
    /// <summary>State-of-charge tolerance, percentage points.</summary>
    public double SocPercent { get; set; } = 0.5;

    /// <summary>Power tolerance, kW.</summary>
    public double PowerKw { get; set; } = 0.1;

    /// <summary>Speed tolerance, km/h.</summary>
    public double SpeedKmh { get; set; } = 0.5;

    /// <summary>Latitude/longitude tolerance, degrees (~1.1 m at 5 decimals).</summary>
    public double LatLonDegrees { get; set; } = 0.00001;
}
