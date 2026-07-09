using FordConnectToAbrpSync.Security;
using Microsoft.Extensions.Logging;

namespace FordConnectToAbrpSync.Ford;

/// <summary>
/// Supplies Ford access tokens to the Run loop: caches the current access token
/// in memory, refreshes it from the persisted refresh token before expiry (or on
/// demand after a 401), and persists any rotated refresh token back to the store.
/// </summary>
internal sealed class FordTokenService(
    FordAuthClient authClient,
    ITokenStore tokenStore,
    ILogger<FordTokenService> logger)
{
    // Refresh a little before the real expiry to avoid racing the clock.
    private static readonly TimeSpan ExpiryLeeway = TimeSpan.FromMinutes(2);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAt = DateTimeOffset.MinValue;

    public sealed class NotAuthenticatedException(string message) : Exception(message);

    public async Task<string> GetAccessTokenAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!forceRefresh
                && _accessToken is not null
                && DateTimeOffset.UtcNow < _accessTokenExpiresAt - ExpiryLeeway)
            {
                return _accessToken;
            }

            var stored = tokenStore.Load()
                ?? throw new NotAuthenticatedException(
                    "No Ford refresh token found. Run the 'login' command first.");

            var token = await authClient.RefreshAsync(stored.RefreshToken, cancellationToken);

            if (string.IsNullOrEmpty(token.AccessToken))
            {
                throw new InvalidOperationException("Ford refresh returned no access token.");
            }

            _accessToken = token.AccessToken;
            _accessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn);

            // Ford rotates the refresh token on some refreshes — persist the new one.
            if (!string.IsNullOrEmpty(token.RefreshToken) && token.RefreshToken != stored.RefreshToken)
            {
                tokenStore.Save(stored with { RefreshToken = token.RefreshToken, ObtainedUtc = DateTimeOffset.UtcNow });
                logger.LogInformation("Ford refresh token rotated and persisted.");
            }

            return _accessToken;
        }
        finally
        {
            _gate.Release();
        }
    }
}
