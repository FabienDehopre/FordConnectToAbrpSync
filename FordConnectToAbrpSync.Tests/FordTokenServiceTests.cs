using System.Net;
using FordConnectToAbrpSync.Configuration;
using FordConnectToAbrpSync.Ford;
using FordConnectToAbrpSync.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FordConnectToAbrpSync.Tests;

public class FordTokenServiceTests
{
    private sealed class FakeTokenStore : ITokenStore
    {
        public StoredToken? Stored { get; set; }
        public int SaveCount { get; private set; }

        public StoredToken? Load() => Stored;

        public void Save(StoredToken token)
        {
            Stored = token;
            SaveCount++;
        }
    }

    private static string TokenBody(string accessToken, string? refreshToken = null, int expiresIn = 3600)
        => $$"""
            {"access_token":"{{accessToken}}"{{(refreshToken is null ? "" : $@",""refresh_token"":""{refreshToken}""")}},"expires_in":{{expiresIn}},"token_type":"Bearer"}
            """;

    private static FordTokenService CreateService(StubHttpMessageHandler stub, FakeTokenStore store)
        => new(
            new FordAuthClient(
                new HttpClient(stub),
                Options.Create(new FordOptions
                {
                    ClientId = "cid",
                    ClientSecret = "sec",
                    TokenUrl = "https://auth.example/token",
                })),
            store,
            NullLogger<FordTokenService>.Instance);

    [Test]
    public async Task Throws_WhenNoStoredToken()
    {
        var service = CreateService(new StubHttpMessageHandler(), new FakeTokenStore());

        await Assert.That(async () => await service.GetAccessTokenAsync(forceRefresh: false, CancellationToken.None))
            .Throws<FordTokenService.NotAuthenticatedException>();
    }

    [Test]
    public async Task CachesAccessToken_UntilExpiry()
    {
        var stub = new StubHttpMessageHandler().Enqueue(HttpStatusCode.OK, TokenBody("at-1"));
        var store = new FakeTokenStore { Stored = new StoredToken { RefreshToken = "rt" } };
        var service = CreateService(stub, store);

        var first = await service.GetAccessTokenAsync(forceRefresh: false, CancellationToken.None);
        var second = await service.GetAccessTokenAsync(forceRefresh: false, CancellationToken.None);

        await Assert.That(first).IsEqualTo("at-1");
        await Assert.That(second).IsEqualTo("at-1");
        await Assert.That(stub.Requests).Count().IsEqualTo(1);
    }

    [Test]
    public async Task ForceRefresh_BypassesCache()
    {
        var stub = new StubHttpMessageHandler()
            .Enqueue(HttpStatusCode.OK, TokenBody("at-1"))
            .Enqueue(HttpStatusCode.OK, TokenBody("at-2"));
        var store = new FakeTokenStore { Stored = new StoredToken { RefreshToken = "rt" } };
        var service = CreateService(stub, store);

        await service.GetAccessTokenAsync(forceRefresh: false, CancellationToken.None);
        var refreshed = await service.GetAccessTokenAsync(forceRefresh: true, CancellationToken.None);

        await Assert.That(refreshed).IsEqualTo("at-2");
        await Assert.That(stub.Requests).Count().IsEqualTo(2);
    }

    [Test]
    public async Task RefreshesAgain_WhenTokenAlreadyWithinExpiryLeeway()
    {
        // expires_in 0 puts the token inside the 2-minute leeway immediately.
        var stub = new StubHttpMessageHandler()
            .Enqueue(HttpStatusCode.OK, TokenBody("at-1", expiresIn: 0))
            .Enqueue(HttpStatusCode.OK, TokenBody("at-2"));
        var store = new FakeTokenStore { Stored = new StoredToken { RefreshToken = "rt" } };
        var service = CreateService(stub, store);

        await service.GetAccessTokenAsync(forceRefresh: false, CancellationToken.None);
        var refreshed = await service.GetAccessTokenAsync(forceRefresh: false, CancellationToken.None);

        await Assert.That(refreshed).IsEqualTo("at-2");
        await Assert.That(stub.Requests).Count().IsEqualTo(2);
    }

    [Test]
    public async Task PersistsRotatedRefreshToken()
    {
        var stub = new StubHttpMessageHandler()
            .Enqueue(HttpStatusCode.OK, TokenBody("at-1", refreshToken: "rt-new"));
        var store = new FakeTokenStore { Stored = new StoredToken { RefreshToken = "rt-old" } };
        var service = CreateService(stub, store);

        await service.GetAccessTokenAsync(forceRefresh: false, CancellationToken.None);

        await Assert.That(store.SaveCount).IsEqualTo(1);
        await Assert.That(store.Stored!.RefreshToken).IsEqualTo("rt-new");
    }

    [Test]
    public async Task DoesNotPersist_WhenRefreshTokenUnchanged()
    {
        var stub = new StubHttpMessageHandler()
            .Enqueue(HttpStatusCode.OK, TokenBody("at-1", refreshToken: "rt"));
        var store = new FakeTokenStore { Stored = new StoredToken { RefreshToken = "rt" } };
        var service = CreateService(stub, store);

        await service.GetAccessTokenAsync(forceRefresh: false, CancellationToken.None);

        await Assert.That(store.SaveCount).IsEqualTo(0);
    }

    [Test]
    public async Task Throws_WhenRefreshReturnsNoAccessToken()
    {
        var stub = new StubHttpMessageHandler()
            .Enqueue(HttpStatusCode.OK, """{"refresh_token":"rt","expires_in":3600}""");
        var store = new FakeTokenStore { Stored = new StoredToken { RefreshToken = "rt" } };
        var service = CreateService(stub, store);

        await Assert.That(async () => await service.GetAccessTokenAsync(forceRefresh: false, CancellationToken.None))
            .Throws<InvalidOperationException>();
    }
}
