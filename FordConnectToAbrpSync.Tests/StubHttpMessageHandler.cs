using System.Net;
using System.Text;

namespace FordConnectToAbrpSync.Tests;

/// <summary>
/// Scripted HTTP handler: returns queued responses in order and records every
/// outgoing request (body and bearer token captured at send time, since a
/// retried request object is mutated in place).
/// </summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses = new();

    public List<HttpRequestMessage> Requests { get; } = [];
    public List<string?> RequestBodies { get; } = [];
    public List<string?> BearerTokens { get; } = [];

    public StubHttpMessageHandler Enqueue(HttpStatusCode status, string body)
    {
        _responses.Enqueue(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        });
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        RequestBodies.Add(request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken));
        BearerTokens.Add(request.Headers.Authorization?.Parameter);

        return _responses.Count > 0
            ? _responses.Dequeue()
            : throw new InvalidOperationException("No stubbed response left for this request.");
    }
}
