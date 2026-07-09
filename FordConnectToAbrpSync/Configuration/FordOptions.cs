namespace FordConnectToAbrpSync.Configuration;

internal sealed class FordOptions
{
    public const string SectionName = "Ford";

    /// <summary>Base address of the FordConnect query API.</summary>
    public string BaseUrl { get; set; } = "https://api.vehicle.ford.com/fcon-query/";

    /// <summary>Telemetry endpoint, relative to <see cref="BaseUrl"/>.</summary>
    public string TelemetryPath { get; set; } = "v1/telemetry";

    /// <summary>Ford's public authorize-init proxy in front of Azure AD B2C (interactive Login).</summary>
    public string AuthorizeUrl { get; set; } =
        "https://api.vehicle.ford.com/fcon-public/v1/auth/init";

    /// <summary>Azure AD B2C token endpoint (code exchange + refresh), with the B2C policy as the ?p= query.</summary>
    public string TokenUrl { get; set; } =
        "https://api.vehicle.ford.com/dah2vb2cprod.onmicrosoft.com/oauth2/v2.0/token?p=B2C_1A_FCON_AUTHORIZE";

    /// <summary>
    /// OAuth scope for the token endpoint. Leave empty to use Ford's expected
    /// "{ClientId} offline_access openid"; offline_access is required for a refresh token.
    /// </summary>
    public string Scope { get; set; } = string.Empty;

    /// <summary>Loopback port the interactive Login listens on for the OAuth redirect.</summary>
    public int LoopbackPort { get; set; } = 19579;

    /// <summary>App registration client id. Secret — supply via user-secrets or env.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>App registration client secret. Secret — supply via user-secrets or env.</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Optional Application-Id header some Ford APIs require.</summary>
    public string? ApplicationId { get; set; }

    /// <summary>Where the encrypted refresh token is persisted.</summary>
    public string TokenFilePath { get; set; } = "./data/ford-token.json";

    /// <summary>Data Protection key ring directory.</summary>
    public string KeysDirectory { get; set; } = "./data/keys";
}
