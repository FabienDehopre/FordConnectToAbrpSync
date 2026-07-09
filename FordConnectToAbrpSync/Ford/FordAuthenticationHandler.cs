using System.Net;
using System.Net.Http.Headers;
using FordConnectToAbrpSync.Configuration;
using Microsoft.Extensions.Options;

namespace FordConnectToAbrpSync.Ford;

/// <summary>
/// Injects the Ford bearer token on outgoing telemetry requests. On a 401 it
/// forces a single token refresh and retries once. Placed INSIDE the resilience
/// handler so a genuine 401 isn't retried as a transient fault.
/// </summary>
internal sealed class FordAuthenticationHandler(
    FordTokenService tokenService,
    IOptions<FordOptions> options) : DelegatingHandler
{
    private readonly FordOptions _options = options.Value;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await ApplyAuthAsync(request, forceRefresh: false, cancellationToken);
        var response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        // Access token may have been revoked/expired early — refresh once and retry.
        response.Dispose();
        await ApplyAuthAsync(request, forceRefresh: true, cancellationToken);
        return await base.SendAsync(request, cancellationToken);
    }

    private async Task ApplyAuthAsync(
        HttpRequestMessage request, bool forceRefresh, CancellationToken cancellationToken)
    {
        var accessToken = await tokenService.GetAccessTokenAsync(forceRefresh, cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        if (!string.IsNullOrEmpty(_options.ApplicationId) && !request.Headers.Contains("Application-Id"))
        {
            request.Headers.Add("Application-Id", _options.ApplicationId);
        }
    }
}
