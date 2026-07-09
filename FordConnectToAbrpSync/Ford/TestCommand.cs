using Microsoft.Extensions.Logging;

namespace FordConnectToAbrpSync.Ford;

/// <summary>
/// A diagnostic command that exercises the stored access token and the Ford
/// Connect telemetry fetch, then writes the raw JSON payload to stdout and
/// nothing else — so it can be piped straight into tools like <c>jq</c>.
/// All logging goes to stderr, keeping stdout clean.
/// </summary>
internal sealed class TestCommand(
    FordTelemetryClient telemetryClient,
    ILogger<TestCommand> logger)
{
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            var json = await telemetryClient.GetRawTelemetryAsync(cancellationToken);
            Console.Out.Write(json);
            Console.Out.Write('\n');
            return 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Telemetry fetch failed. Have you run the 'login' command first?");
            return 1;
        }
    }
}
