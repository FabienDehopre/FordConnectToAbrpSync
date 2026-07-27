namespace FordConnectToAbrpSync.Configuration;

/// <summary>
/// Settings for the external Wake/Sleep Signal endpoints.
/// </summary>
internal sealed class SignalOptions
{
    public const string SectionName = "Signal";

    /// <summary>
    /// Shared secret the caller must present as a bearer token. When unset,
    /// the Signal endpoints refuse every request.
    /// </summary>
    public string? Secret { get; set; }
}
