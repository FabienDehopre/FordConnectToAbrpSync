using FordConnectToAbrpSync.Configuration;
using FordConnectToAbrpSync.Sync;

namespace FordConnectToAbrpSync.Tests;

public class SyncPacerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 27, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan BoostWindow = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan LongDelay = TimeSpan.FromMinutes(5);

    private static readonly SyncOptions Options = new()
    {
        Interval = TimeSpan.FromSeconds(60),
        IdleInterval = TimeSpan.FromMinutes(30),
    };

    [Test]
    public async Task WaitAsync_NoSignal_TimesOut()
    {
        var pacer = new SyncPacer();

        var signalled = await pacer.WaitAsync(TimeSpan.FromMilliseconds(10), CancellationToken.None);

        await Assert.That(signalled).IsFalse();
    }

    [Test]
    public async Task WaitAsync_WakeDuringWait_ReturnsEarly()
    {
        var pacer = new SyncPacer();

        var wait = pacer.WaitAsync(LongDelay, CancellationToken.None);
        pacer.Wake(Now, BoostWindow);

        var signalled = await wait.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(signalled).IsTrue();
    }

    [Test]
    public async Task WaitAsync_WakeBeforeWait_IsLatched()
    {
        var pacer = new SyncPacer();

        // Signal lands while a Sync Cycle is running (no waiter yet).
        pacer.Wake(Now, BoostWindow);

        var signalled = await pacer.WaitAsync(LongDelay, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(signalled).IsTrue();
    }

    [Test]
    public async Task WaitAsync_SignalIsConsumed_NextWaitTimesOut()
    {
        var pacer = new SyncPacer();
        pacer.Wake(Now, BoostWindow);
        await pacer.WaitAsync(LongDelay, CancellationToken.None);

        var second = await pacer.WaitAsync(TimeSpan.FromMilliseconds(10), CancellationToken.None);

        await Assert.That(second).IsFalse();
    }

    [Test]
    public async Task WaitAsync_Cancelled_Throws()
    {
        var pacer = new SyncPacer();
        using var cts = new CancellationTokenSource();

        var wait = pacer.WaitAsync(LongDelay, cts.Token);
        await cts.CancelAsync();

        await Assert.That(async () => await wait).Throws<OperationCanceledException>();
    }

    [Test]
    public async Task IsBoosted_AfterWake_TrueUntilWindowExpires()
    {
        var pacer = new SyncPacer();

        pacer.Wake(Now, BoostWindow);

        await Assert.That(pacer.IsBoosted(Now)).IsTrue();
        await Assert.That(pacer.IsBoosted(Now + BoostWindow - TimeSpan.FromSeconds(1))).IsTrue();
        await Assert.That(pacer.IsBoosted(Now + BoostWindow)).IsFalse();
    }

    [Test]
    public async Task IsBoosted_WithoutWake_False()
    {
        var pacer = new SyncPacer();

        await Assert.That(pacer.IsBoosted(Now)).IsFalse();
    }

    [Test]
    public async Task Sleep_ClosesBoostWindow()
    {
        var pacer = new SyncPacer();
        pacer.Wake(Now, BoostWindow);

        pacer.Sleep();

        await Assert.That(pacer.IsBoosted(Now)).IsFalse();
    }

    [Test]
    public async Task Sleep_AlsoInterruptsWait()
    {
        var pacer = new SyncPacer();

        var wait = pacer.WaitAsync(LongDelay, CancellationToken.None);
        pacer.Sleep();

        var signalled = await wait.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(signalled).IsTrue();
    }

    [Test]
    public async Task SelectWait_Active_UsesNormalInterval()
    {
        var pacer = new SyncPacer();

        var (wait, idlePace) = pacer.SelectWait(isIdle: false, Options, Now);

        await Assert.That(wait).IsEqualTo(Options.Interval);
        await Assert.That(idlePace).IsFalse();
    }

    [Test]
    public async Task SelectWait_IdleWithoutBoost_UsesIdleInterval()
    {
        var pacer = new SyncPacer();

        var (wait, idlePace) = pacer.SelectWait(isIdle: true, Options, Now);

        await Assert.That(wait).IsEqualTo(Options.IdleInterval);
        await Assert.That(idlePace).IsTrue();
    }

    [Test]
    public async Task SelectWait_IdleDuringBoostWindow_UsesNormalInterval()
    {
        var pacer = new SyncPacer();
        pacer.Wake(Now, BoostWindow);

        var (wait, idlePace) = pacer.SelectWait(isIdle: true, Options, Now);

        await Assert.That(wait).IsEqualTo(Options.Interval);
        await Assert.That(idlePace).IsFalse();
    }

    [Test]
    public async Task SelectWait_IdleAfterBoostWindowExpires_UsesIdleInterval()
    {
        var pacer = new SyncPacer();
        pacer.Wake(Now, BoostWindow);

        var (wait, _) = pacer.SelectWait(isIdle: true, Options, Now + BoostWindow);

        await Assert.That(wait).IsEqualTo(Options.IdleInterval);
    }
}
