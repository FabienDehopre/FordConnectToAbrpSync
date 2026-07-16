using System.Net;
using FordConnectToAbrpSync.Configuration;
using FordConnectToAbrpSync.Ford;
using FordConnectToAbrpSync.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FordConnectToAbrpSync.Tests;

public class FordAuthenticationHandlerTests
{
    private sealed class FakeTokenStore : ITokenStore
    {
        public StoredToken? Stored { get; set; }
        public StoredToken? Load() => Stored;
        public void Save(StoredToken token) => Stored = token;
    }

    /// <summary>
    /// Wires the handler over a scripted inner handler. The token service is
    /// backed by its own scripted auth endpoint returning at-1, then at-2.
    /// </summary>
    private static (HttpClient Client, StubHttpMessageHandler Inner, StubHttpMessageHandler Auth) CreateChain(
        string? applicationId = "app-id")
    {
        var auth = new StubHttpMessageHandler()
            .Enqueue(HttpStatusCode.OK, """{"access_token":"at-1","expires_in":3600}""")
            .Enqueue(HttpStatusCode.OK, """{"access_token":"at-2","expires_in":3600}""");

        var options = Options.Create(new FordOptions
        {
            ClientId = "cid",
            ClientSecret = "sec",
            TokenUrl = "https://auth.example/token",
            ApplicationId = applicationId,
        });

        var tokenService = new FordTokenService(
            new FordAuthClient(new HttpClient(auth), options),
            new FakeTokenStore { Stored = new StoredToken { RefreshToken = "rt" } },
            NullLogger<FordTokenService>.Instance);

        var inner = new StubHttpMessageHandler();
        var handler = new FordAuthenticationHandler(tokenService, options) { InnerHandler = inner };
        return (new HttpClient(handler), inner, auth);
    }

    [Test]
    public async Task AttachesBearerToken_AndApplicationIdHeader()
    {
        var (client, inner, _) = CreateChain();
        inner.Enqueue(HttpStatusCode.OK, "{}");

        var response = await client.GetAsync("https://ford.example/v1/telemetry");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(inner.Requests).Count().IsEqualTo(1);
        await Assert.That(inner.BearerTokens[0]).IsEqualTo("at-1");
        await Assert.That(inner.Requests[0].Headers.GetValues("Application-Id").Single()).IsEqualTo("app-id");
    }

    [Test]
    public async Task OmitsApplicationIdHeader_WhenNotConfigured()
    {
        var (client, inner, _) = CreateChain(applicationId: null);
        inner.Enqueue(HttpStatusCode.OK, "{}");

        await client.GetAsync("https://ford.example/v1/telemetry");

        await Assert.That(inner.Requests[0].Headers.Contains("Application-Id")).IsFalse();
    }

    [Test]
    public async Task On401_ForcesRefreshAndRetriesOnce_WithNewToken()
    {
        var (client, inner, auth) = CreateChain();
        inner.Enqueue(HttpStatusCode.Unauthorized, "{}")
             .Enqueue(HttpStatusCode.OK, "{}");

        var response = await client.GetAsync("https://ford.example/v1/telemetry");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(inner.Requests).Count().IsEqualTo(2);
        // Second attempt carries the force-refreshed token.
        await Assert.That(inner.BearerTokens[0]).IsEqualTo("at-1");
        await Assert.That(inner.BearerTokens[1]).IsEqualTo("at-2");
        await Assert.That(auth.Requests).Count().IsEqualTo(2);
    }

    [Test]
    public async Task Non401Error_IsReturnedWithoutRetry()
    {
        var (client, inner, auth) = CreateChain();
        inner.Enqueue(HttpStatusCode.InternalServerError, "{}");

        var response = await client.GetAsync("https://ford.example/v1/telemetry");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.InternalServerError);
        await Assert.That(inner.Requests).Count().IsEqualTo(1);
        await Assert.That(auth.Requests).Count().IsEqualTo(1);
    }
}
