using FordConnectToAbrpSync.Health;

namespace FordConnectToAbrpSync.Tests;

public class HeartbeatCheckTests
{
    private static string NewPath() =>
        Path.Combine(Path.GetTempPath(), $"fordsync-heartbeat-{Guid.NewGuid():N}");

    [Test]
    public async Task Run_DeadlineInFuture_ReturnsTrue()
    {
        var path = NewPath();
        var now = new DateTimeOffset(2026, 7, 25, 12, 0, 0, TimeSpan.Zero);
        await File.WriteAllTextAsync(path, now.AddMinutes(1).ToString("o"));
        try
        {
            await Assert.That(HeartbeatCheck.Run(path, now)).IsTrue();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task Run_DeadlineInPast_ReturnsFalse()
    {
        var path = NewPath();
        var now = new DateTimeOffset(2026, 7, 25, 12, 0, 0, TimeSpan.Zero);
        await File.WriteAllTextAsync(path, now.AddMinutes(-1).ToString("o"));
        try
        {
            await Assert.That(HeartbeatCheck.Run(path, now)).IsFalse();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task Run_FileMissing_ReturnsFalse()
    {
        var path = NewPath();
        var now = new DateTimeOffset(2026, 7, 25, 12, 0, 0, TimeSpan.Zero);

        await Assert.That(HeartbeatCheck.Run(path, now)).IsFalse();
    }

    [Test]
    public async Task Run_PathIsDirectory_ReturnsFalse()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"fordsync-heartbeat-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var now = new DateTimeOffset(2026, 7, 25, 12, 0, 0, TimeSpan.Zero);
        try
        {
            await Assert.That(HeartbeatCheck.Run(dir, now)).IsFalse();
        }
        finally
        {
            Directory.Delete(dir);
        }
    }

    [Test]
    public async Task Run_ContentUnparseable_ReturnsFalse()
    {
        var path = NewPath();
        var now = new DateTimeOffset(2026, 7, 25, 12, 0, 0, TimeSpan.Zero);
        await File.WriteAllTextAsync(path, "not-a-timestamp");
        try
        {
            await Assert.That(HeartbeatCheck.Run(path, now)).IsFalse();
        }
        finally
        {
            File.Delete(path);
        }
    }
}
