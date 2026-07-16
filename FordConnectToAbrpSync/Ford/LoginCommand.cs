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
/// loopback redirect, exchanges the code for tokens, and persists the
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

        if (_options.LoopbackPort is < 1024 or > 49151)
        {
            logger.LogError(
                "Ford:LoopbackPort ({Port}) is out of range. Set a value between 1024 and 49151 "
                + "(avoid privileged ports below 1024 and the ephemeral range above 49151).",
                _options.LoopbackPort);
            return 1;
        }

        var redirectUri = $"http://localhost:{_options.LoopbackPort}/";
        var state = CreateState();

        var authorizeUrl = BuildAuthorizeUrl(redirectUri, state);

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

        var token = await authClient.ExchangeCodeAsync(code, redirectUri, cancellationToken);

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

    private string BuildAuthorizeUrl(string redirectUri, string state)
    {
        // Ford's /fcon-public/v1/auth/init proxy fronts the Azure AD B2C authorize
        // policy: it wants only response_type/client_id/redirect_uri/state (no PKCE,
        // no scope — those are baked into the B2C_1A_FCON_AUTHORIZE policy).
        var query = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = _options.ClientId,
            ["redirect_uri"] = redirectUri,
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

    // The auth/init proxy caps state at 16 bytes, so keep it short: 8 random bytes
    // as 16 hex chars.
    private static string CreateState() => Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();

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
