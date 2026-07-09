using System.Net;
using System.Text;
using FordConnectToAbrpSync.Abrp;
using FordConnectToAbrpSync.Configuration;
using FordConnectToAbrpSync.Ford;
using Microsoft.Extensions.Options;

namespace FordConnectToAbrpSync.Tests;

public class HttpClientTests
{
    /// <summary>Captures the outgoing request and returns a canned response.</summary>
    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (request.Content is not null)
            {
                LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            return new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        }
    }

    [Test]
    public async Task AbrpClient_Send_PostsTlmFormWithKeysInQuery_AndReturnsTrueOnOk()
    {
        var stub = new StubHandler(HttpStatusCode.OK, """{"status":"ok"}""");
        var client = new AbrpClient(
            new HttpClient(stub) { BaseAddress = new Uri("https://api.iternio.com/") },
            Options.Create(new AbrpOptions { ApiKey = "KEY 1", Token = "tok", SendPath = "1/tlm/send" }));

        var ok = await client.SendAsync(new AbrpTelemetry { Utc = 123, Soc = 55 }, CancellationToken.None);

        await Assert.That(ok).IsTrue();
        await Assert.That(stub.LastRequest!.Method).IsEqualTo(HttpMethod.Post);
        var uri = stub.LastRequest!.RequestUri!.AbsoluteUri;
        await Assert.That(uri).Contains("1/tlm/send");
        await Assert.That(uri).Contains("api_key=KEY%201");
        await Assert.That(uri).Contains("token=tok");
        // Body carries the tlm as a form field with the serialized payload.
        await Assert.That(stub.LastRequestBody!).Contains("tlm=");
        await Assert.That(stub.LastRequestBody!).Contains("soc");
    }

    [Test]
    public async Task AbrpClient_Send_ReturnsFalse_OnErrorStatusBody()
    {
        var stub = new StubHandler(HttpStatusCode.OK, """{"status":"error","error":"bad token"}""");
        var client = new AbrpClient(
            new HttpClient(stub) { BaseAddress = new Uri("https://api.iternio.com/") },
            Options.Create(new AbrpOptions { ApiKey = "k", Token = "t" }));

        var ok = await client.SendAsync(new AbrpTelemetry { Utc = 1 }, CancellationToken.None);

        await Assert.That(ok).IsFalse();
    }

    [Test]
    public async Task AbrpClient_Send_ThrowsOnHttpError()
    {
        var stub = new StubHandler(HttpStatusCode.TooManyRequests, "rate limited");
        var client = new AbrpClient(
            new HttpClient(stub) { BaseAddress = new Uri("https://api.iternio.com/") },
            Options.Create(new AbrpOptions { ApiKey = "k", Token = "t" }));

        await Assert.That(async () =>
                await client.SendAsync(new AbrpTelemetry { Utc = 1 }, CancellationToken.None))
            .Throws<HttpRequestException>();
    }

    [Test]
    public async Task FordTelemetryClient_Get_DeserializesSnapshot()
    {
        const string body = """
        {
          "updateTime": "2025-08-15T21:53:06Z",
          "metrics": { "xevBatteryStateOfCharge": { "value": 80.0 }, "gearLeverPosition": { "value": "PARK" } }
        }
        """;
        var stub = new StubHandler(HttpStatusCode.OK, body);
        var client = new FordTelemetryClient(
            new HttpClient(stub) { BaseAddress = new Uri("https://api.vehicle.ford.com/fcon-query/") },
            Options.Create(new FordOptions()));

        var snapshot = await client.GetTelemetryAsync(CancellationToken.None);

        await Assert.That(snapshot).IsNotNull();
        await Assert.That(snapshot!.Metrics!.XevBatteryStateOfCharge!.Value).IsEqualTo(80.0);
        await Assert.That(stub.LastRequest!.Method).IsEqualTo(HttpMethod.Get);
        await Assert.That(stub.LastRequest!.RequestUri!.ToString()).EndsWith("/v1/telemetry");
    }
}
