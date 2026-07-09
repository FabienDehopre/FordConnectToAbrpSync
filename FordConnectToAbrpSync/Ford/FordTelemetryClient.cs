using System.Text.Json;
using FordConnectToAbrpSync.Configuration;
using FordConnectToAbrpSync.Serialization;
using Microsoft.Extensions.Options;

namespace FordConnectToAbrpSync.Ford;

/// <summary>
/// Fetches a Telemetry Snapshot from Ford's /v1/telemetry. The bearer auth and
/// resilience handlers are attached to the injected <see cref="HttpClient"/>.
/// The endpoint takes no parameters — it returns whatever vehicle the user
/// authorized in the FordPass portal.
/// </summary>
internal sealed class FordTelemetryClient(HttpClient httpClient, IOptions<FordOptions> options)
{
    private readonly string _telemetryPath = options.Value.TelemetryPath;

    public async Task<FordTelemetryResponse?> GetTelemetryAsync(CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(_telemetryPath, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync(
            stream, AppJsonSerializerContext.Default.FordTelemetryResponse, cancellationToken);
    }

    /// <summary>
    /// Fetches the Telemetry Snapshot as the raw response body, unparsed. Used by
    /// the <c>test</c> command so the exact Ford payload can be piped to tools
    /// like <c>jq</c>.
    /// </summary>
    public async Task<string> GetRawTelemetryAsync(CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(_telemetryPath, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }
}
