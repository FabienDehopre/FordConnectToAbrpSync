using System.Text.Json;
using FordConnectToAbrpSync.Configuration;
using FordConnectToAbrpSync.Serialization;
using Microsoft.Extensions.Options;

namespace FordConnectToAbrpSync.Ford;

/// <summary>
/// Talks to the Ford (Azure AD B2C) OAuth token endpoint: exchanges an
/// authorization code (Login) and refreshes access tokens (Run). Uses its own
/// HttpClient WITHOUT the bearer auth handler to avoid recursion.
/// </summary>
internal sealed class FordAuthClient(HttpClient httpClient, IOptions<FordOptions> options)
{
    private readonly FordOptions _options = options.Value;

    /// <summary>
    /// Scope sent to the token endpoint. Ford's Azure AD B2C expects the app's own
    /// client id as a scope, e.g. "{clientId} offline_access openid". Overridable
    /// via Ford:Scope; falls back to the client-id form when left empty.
    /// </summary>
    private string EffectiveScope => string.IsNullOrWhiteSpace(_options.Scope)
        ? $"{_options.ClientId} offline_access openid"
        : _options.Scope;

    public Task<FordTokenResponse> ExchangeCodeAsync(
        string code, string redirectUri, CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = _options.ClientId,
            ["client_secret"] = _options.ClientSecret,
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["scope"] = EffectiveScope,
        };
        return PostTokenAsync(form, cancellationToken);
    }

    public Task<FordTokenResponse> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = _options.ClientId,
            ["client_secret"] = _options.ClientSecret,
            ["refresh_token"] = refreshToken,
            ["scope"] = EffectiveScope,
        };
        return PostTokenAsync(form, cancellationToken);
    }

    private async Task<FordTokenResponse> PostTokenAsync(
        Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await httpClient.PostAsync(_options.TokenUrl, content, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var token = await JsonSerializer.DeserializeAsync(
            stream, AppJsonSerializerContext.Default.FordTokenResponse, cancellationToken);

        return token ?? throw new InvalidOperationException("Ford token endpoint returned an empty body.");
    }
}
