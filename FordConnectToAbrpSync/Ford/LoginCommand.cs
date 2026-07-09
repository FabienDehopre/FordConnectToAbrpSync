using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using FordConnectToAbrpSync.Configuration;
using FordConnectToAbrpSync.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FordConnectToAbrpSync.Ford;

/// <summary>
/// The one-time interactive Login: opens the Ford authorize page, catches the
/// loopback redirect, exchanges the code (PKCE) for tokens, and persists the
/// refresh token to the Token Store. Run once before the headless Run.
/// </summary>
internal sealed class LoginCommand(
    FordAuthClient authClient,
    ITokenStore tokenStore,
    IOptions<FordOptions> options,
    ILogger<LoginCommand> logger)
{
    private readonly FordOptions _options = options.Value;

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_options.ClientId))
        {
            logger.LogError("Ford:ClientId is not configured. Set it via user-secrets or environment.");
            return 1;
        }

        if (_options.LoopbackPort is < 1 or > 65535)
        {
            logger.LogError(
                "Ford:LoopbackPort ({Port}) is out of range. Set a value between 1 and 65535.",
                _options.LoopbackPort);
            return 1;
        }

        var redirectUri = $"http://localhost:{_options.LoopbackPort}/";
        var verifier = CreateCodeVerifier();
        var challenge = CreateCodeChallenge(verifier);
        var state = CreateCodeVerifier();

        var authorizeUrl = BuildAuthorizeUrl(redirectUri, challenge, state);

        using var listener = new HttpListener();
        listener.Prefixes.Add(redirectUri);
        try
        {
            listener.Start();
        }
        catch (HttpListenerException ex)
        {
            logger.LogError(
                ex,
                "Could not listen on {RedirectUri}. Port {Port} may be in use. "
                + "Override it via Ford:LoopbackPort (e.g. Ford__LoopbackPort=12345).",
                redirectUri,
                _options.LoopbackPort);
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine("Open this URL in your browser to authorize the app with Ford:");
        Console.WriteLine();
        Console.WriteLine(authorizeUrl);
        Console.WriteLine();
        TryOpenBrowser(authorizeUrl);
        Console.WriteLine($"Waiting for the redirect on {redirectUri} ...");

        var (code, returnedState) = await WaitForRedirectAsync(listener, cancellationToken);

        if (returnedState != state)
        {
            logger.LogError("OAuth state mismatch — aborting login.");
            return 1;
        }

        if (string.IsNullOrEmpty(code))
        {
            logger.LogError("No authorization code was returned.");
            return 1;
        }

        var token = await authClient.ExchangeCodeAsync(code, redirectUri, verifier, cancellationToken);

        if (string.IsNullOrEmpty(token.RefreshToken))
        {
            logger.LogError("Ford returned no refresh token. Ensure the scope includes 'offline_access'.");
            return 1;
        }

        tokenStore.Save(new StoredToken
        {
            RefreshToken = token.RefreshToken,
            ObtainedUtc = DateTimeOffset.UtcNow,
        });

        Console.WriteLine("Login successful. Refresh token saved. You can now run the worker.");
        return 0;
    }

    private string BuildAuthorizeUrl(string redirectUri, string challenge, string state)
    {
        var query = new Dictionary<string, string>
        {
            ["client_id"] = _options.ClientId,
            ["response_type"] = "code",
            ["redirect_uri"] = redirectUri,
            ["response_mode"] = "query",
            ["scope"] = _options.Scope,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            ["state"] = state,
        };
        var qs = string.Join('&', query.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"));
        return $"{_options.AuthorizeUrl}?{qs}";
    }

    private static async Task<(string? Code, string? State)> WaitForRedirectAsync(
        HttpListener listener, CancellationToken cancellationToken)
    {
        var contextTask = listener.GetContextAsync();
        var context = await contextTask.WaitAsync(cancellationToken);

        var code = context.Request.QueryString["code"];
        var state = context.Request.QueryString["state"];
        var error = context.Request.QueryString["error"];

        var message = error is null
            ? "Authorization complete. You can close this tab and return to the terminal."
            : $"Authorization failed: {error}. You can close this tab.";

        var buffer = Encoding.UTF8.GetBytes($"<html><body><h3>{message}</h3></body></html>");
        context.Response.ContentType = "text/html";
        context.Response.ContentLength64 = buffer.Length;
        await context.Response.OutputStream.WriteAsync(buffer, cancellationToken);
        context.Response.Close();

        return (code, state);
    }

    private static string CreateCodeVerifier() => Base64Url(RandomNumberGenerator.GetBytes(32));

    private static string CreateCodeChallenge(string verifier) =>
        Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private void TryOpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not auto-open a browser; open the URL manually.");
        }
    }
}
