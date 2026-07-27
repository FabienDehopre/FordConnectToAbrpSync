using System.Security.Cryptography;
using System.Text;

namespace FordConnectToAbrpSync.Security;

/// <summary>
/// Validates the bearer token on Wake/Sleep Signal requests against the
/// configured shared secret. No configured secret means no request is ever
/// authorized — the endpoints fail closed rather than open.
/// </summary>
internal static class SignalAuthenticator
{
    private const string BearerPrefix = "Bearer ";

    public static bool IsAuthorized(string? configuredSecret, string? authorizationHeader)
    {
        if (string.IsNullOrWhiteSpace(configuredSecret)
            || authorizationHeader is null
            || !authorizationHeader.StartsWith(BearerPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var presented = authorizationHeader[BearerPrefix.Length..];
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(presented),
            Encoding.UTF8.GetBytes(configuredSecret));
    }
}
