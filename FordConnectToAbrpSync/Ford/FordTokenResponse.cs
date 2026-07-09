using System.Text.Json.Serialization;

namespace FordConnectToAbrpSync.Ford;

/// <summary>OAuth token endpoint response (snake_case wire names).</summary>
internal sealed record FordTokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; init; }

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; init; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; init; }

    [JsonPropertyName("token_type")]
    public string? TokenType { get; init; }
}
