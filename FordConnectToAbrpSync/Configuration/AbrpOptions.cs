namespace FordConnectToAbrpSync.Configuration;

internal sealed class AbrpOptions
{
    public const string SectionName = "Abrp";

    /// <summary>Base address of the Iternio/ABRP API.</summary>
    public string BaseUrl { get; set; } = "https://api.iternio.com/";

    /// <summary>Telemetry send endpoint, relative to <see cref="BaseUrl"/>.</summary>
    public string SendPath { get; set; } = "1/tlm/send";

    /// <summary>Partner API key. Secret — supply via user-secrets or env.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Per-vehicle user token from ABRP. Secret — supply via user-secrets or env.</summary>
    public string Token { get; set; } = string.Empty;
}
