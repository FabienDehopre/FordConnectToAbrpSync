using FordConnectToAbrpSync.Health;

namespace FordConnectToAbrpSync.Tests;

public class HeartbeatWriterTests
{
    private static string NewPath() =>
        Path.Combine(Path.GetTempPath(), $"fordsync-heartbeat-{Guid.NewGuid():N}");

    [Test]
    public async Task Touch_WritesDeadlineThreeIntervalsOut()
    {
        var path = NewPath();
        var writer = new HeartbeatWriter(path);
        var now = new DateTimeOffset(2026, 7, 25, 12, 0, 0, TimeSpan.Zero);
        var interval = TimeSpan.FromSeconds(60);
        try
        {
            writer.Touch(now, interval);

            await Assert.That(HeartbeatCheck.Run(path, now.Add(interval * 3).AddSeconds(-1))).IsTrue();
            await Assert.That(HeartbeatCheck.Run(path, now.Add(interval * 3).AddSeconds(1))).IsFalse();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task Touch_LeavesNoTempFileResidue()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"fordsync-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "heartbeat");
        var writer = new HeartbeatWriter(path);
        try
        {
            writer.Touch(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(60));

            await Assert.That(Directory.GetFiles(dir)).IsEquivalentTo([path]);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Test]
    public async Task Touch_CreatesMissingDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"fordsync-{Guid.NewGuid():N}", "nested");
        var path = Path.Combine(dir, "heartbeat");
        var writer = new HeartbeatWriter(path);
        try
        {
            writer.Touch(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(60));

            await Assert.That(File.Exists(path)).IsTrue();
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(dir)!, recursive: true);
        }
    }
}
