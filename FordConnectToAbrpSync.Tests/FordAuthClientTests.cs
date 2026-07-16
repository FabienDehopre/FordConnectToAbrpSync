using System.Net;
using FordConnectToAbrpSync.Configuration;
using FordConnectToAbrpSync.Ford;
using Microsoft.Extensions.Options;

namespace FordConnectToAbrpSync.Tests;

public class FordAuthClientTests
{
    private const string TokenUrl = "https://auth.example/oauth2/token?p=B2C_1A_TEST";

    private const string TokenBody = """
        {"access_token":"at-1","refresh_token":"rt-1","expires_in":3600,"token_type":"Bearer"}
        """;

    private static FordAuthClient CreateClient(StubHttpMessageHandler stub, string scope = "")
        => new(
            new HttpClient(stub),
            Options.Create(new FordOptions
            {
                ClientId = "cid",
                ClientSecret = "sec",
                TokenUrl = TokenUrl,
                Scope = scope,
            }));

    [Test]
    public async Task Refresh_PostsRefreshGrantForm_AndDeserializesResponse()
    {
        var stub = new StubHttpMessageHandler().Enqueue(HttpStatusCode.OK, TokenBody);
        var client = CreateClient(stub);

        var token = await client.RefreshAsync("old-refresh", CancellationToken.None);

        await Assert.That(stub.Requests[0].Method).IsEqualTo(HttpMethod.Post);
        await Assert.That(stub.Requests[0].RequestUri!.AbsoluteUri).IsEqualTo(TokenUrl);
        var body = stub.RequestBodies[0]!;
        await Assert.That(body).Contains("grant_type=refresh_token");
        await Assert.That(body).Contains("client_id=cid");
        await Assert.That(body).Contains("client_secret=sec");
        await Assert.That(body).Contains("refresh_token=old-refresh");
        await Assert.That(token.AccessToken).IsEqualTo("at-1");
        await Assert.That(token.RefreshToken).IsEqualTo("rt-1");
        await Assert.That(token.ExpiresIn).IsEqualTo(3600);
    }

    [Test]
    public async Task ExchangeCode_PostsAuthorizationCodeGrantForm()
    {
        var stub = new StubHttpMessageHandler().Enqueue(HttpStatusCode.OK, TokenBody);
        var client = CreateClient(stub);

        await client.ExchangeCodeAsync("the-code", "http://localhost:19579/callback", CancellationToken.None);

        var body = stub.RequestBodies[0]!;
        await Assert.That(body).Contains("grant_type=authorization_code");
        await Assert.That(body).Contains("code=the-code");
        await Assert.That(body).Contains("redirect_uri=http%3A%2F%2Flocalhost%3A19579%2Fcallback");
    }

    [Test]
    public async Task EmptyScope_FallsBackToClientIdScopeForm()
    {
        var stub = new StubHttpMessageHandler().Enqueue(HttpStatusCode.OK, TokenBody);
        var client = CreateClient(stub);

        await client.RefreshAsync("rt", CancellationToken.None);

        await Assert.That(stub.RequestBodies[0]!).Contains("scope=cid+offline_access+openid");
    }

    [Test]
    public async Task ConfiguredScope_OverridesFallback()
    {
        var stub = new StubHttpMessageHandler().Enqueue(HttpStatusCode.OK, TokenBody);
        var client = CreateClient(stub, scope: "custom-scope");

        await client.RefreshAsync("rt", CancellationToken.None);

        await Assert.That(stub.RequestBodies[0]!).Contains("scope=custom-scope");
    }

    [Test]
    public async Task Refresh_ThrowsOnHttpError()
    {
        var stub = new StubHttpMessageHandler()
            .Enqueue(HttpStatusCode.BadRequest, """{"error":"invalid_grant"}""");
        var client = CreateClient(stub);

        await Assert.That(async () => await client.RefreshAsync("rt", CancellationToken.None))
            .Throws<HttpRequestException>();
    }

    [Test]
    public async Task Refresh_ThrowsOnNullBody()
    {
        var stub = new StubHttpMessageHandler().Enqueue(HttpStatusCode.OK, "null");
        var client = CreateClient(stub);

        await Assert.That(async () => await client.RefreshAsync("rt", CancellationToken.None))
            .Throws<InvalidOperationException>();
    }
}
