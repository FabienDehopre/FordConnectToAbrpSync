using System.Text.Json;
using FordConnectToAbrpSync.Configuration;
using FordConnectToAbrpSync.Serialization;
using Microsoft.Extensions.Options;

namespace FordConnectToAbrpSync.Abrp;

/// <summary>
/// Sends a mapped Snapshot to the ABRP /tlm/send endpoint. The resilience handler
/// (retry, rate-limit backoff) is attached to the injected <see cref="HttpClient"/>.
/// </summary>
internal sealed class AbrpClient(HttpClient httpClient, IOptions<AbrpOptions> options)
{
    private readonly AbrpOptions _options = options.Value;

    /// <summary>Relays the payload. Returns true when ABRP acknowledges with status "ok".</summary>
    public async Task<bool> SendAsync(AbrpTelemetry telemetry, CancellationToken cancellationToken)
    {
        var tlmJson = JsonSerializer.Serialize(telemetry, AppJsonSerializerContext.Default.AbrpTelemetry);

        var url = $"{_options.SendPath}?api_key={Uri.EscapeDataString(_options.ApiKey)}"
                  + $"&token={Uri.EscapeDataString(_options.Token)}";

        using var content = new FormUrlEncodedContent(
            [new KeyValuePair<string, string>("tlm", tlmJson)]);

        using var response = await httpClient.PostAsync(url, content, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var body = await JsonSerializer.DeserializeAsync(
            stream, AppJsonSerializerContext.Default.AbrpSendResponse, cancellationToken);

        return string.Equals(body?.Status, "ok", StringComparison.OrdinalIgnoreCase);
    }
}
