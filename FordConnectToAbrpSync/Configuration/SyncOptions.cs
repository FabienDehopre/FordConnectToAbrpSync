using FordConnectToAbrpSync.Sync;

namespace FordConnectToAbrpSync.Configuration;

internal sealed class SyncOptions
{
    public const string SectionName = "Sync";

    /// <summary>Poll interval. Re-read each cycle, so changes take effect live.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Flip if the Ford battery-current sign is opposite ABRP's convention.</summary>
    public bool InvertPowerSign { get; set; }

    public ChangeTolerances Tolerances { get; set; } = new();
}
