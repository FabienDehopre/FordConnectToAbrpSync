namespace FordConnectToAbrpSync.Security;

/// <summary>
/// The persisted Ford refresh credential. Rewritten whenever Ford rotates the
/// refresh token.
/// </summary>
internal sealed record StoredToken
{
    public required string RefreshToken { get; init; }

    public DateTimeOffset ObtainedUtc { get; init; }
}

internal interface ITokenStore
{
    /// <summary>Returns the stored token, or null if no Login has happened yet.</summary>
    StoredToken? Load();

    /// <summary>Persists the token, replacing any existing one atomically.</summary>
    void Save(StoredToken token);
}
