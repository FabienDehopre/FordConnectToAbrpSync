namespace FordConnectToAbrpSync.Health;

/// <summary>
/// Records that the Sync Cycle loop is still alive. The deadline is set three
/// intervals out so a single failed or slow cycle doesn't flip the healthcheck —
/// this is a liveness signal, not a "last Sync Cycle succeeded" signal.
/// </summary>
internal sealed class HeartbeatWriter(string path)
{
    public void Touch(DateTimeOffset now, TimeSpan interval)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var deadline = now + (interval * 3);
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(tempPath, deadline.ToString("o"));
        File.Move(tempPath, path, overwrite: true);
    }
}
