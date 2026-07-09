using FordConnectToAbrpSync.Abrp;
using FordConnectToAbrpSync.Sync;

namespace FordConnectToAbrpSync.Tests;

public class SyncDeciderTests
{
    private static readonly DateTimeOffset T1 = new(2025, 8, 15, 21, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T2 = new(2025, 8, 15, 21, 1, 0, TimeSpan.Zero);

    private static SyncDecider NewDecider() => new(new ChangeTolerances());

    private static AbrpTelemetry Tlm(
        double? soc = 60, double? lat = 50.0, double? lon = 4.0,
        double? power = null, double? speed = null,
        bool? charging = null, bool? dcfc = null, bool? parked = null) => new()
        {
            Utc = 1,
            Soc = soc,
            Lat = lat,
            Lon = lon,
            Power = power,
            Speed = speed,
            IsCharging = charging,
            IsDcfc = dcfc,
            IsParked = parked,
        };

    [Test]
    public async Task Decide_FirstSnapshot_Relays()
    {
        var decider = NewDecider();

        var decision = decider.Decide(T1, Tlm());

        await Assert.That(decision).IsEqualTo(SyncDecision.Relay);
    }

    [Test]
    public async Task Decide_SameUpdateTime_IsStale()
    {
        var decider = NewDecider();
        decider.Decide(T1, Tlm());

        var decision = decider.Decide(T1, Tlm(soc: 99));

        await Assert.That(decision).IsEqualTo(SyncDecision.SkipStale);
    }

    [Test]
    public async Task Decide_NullUpdateTime_NeverStale()
    {
        var decider = NewDecider();
        var tlm = Tlm();

        var first = decider.Decide(null, tlm);
        decider.CommitRelay(tlm);
        var second = decider.Decide(null, Tlm());

        await Assert.That(first).IsEqualTo(SyncDecision.Relay);
        // Not stale (can't tell), and unchanged since Relay, so no-change rather than stale.
        await Assert.That(second).IsEqualTo(SyncDecision.SkipNoChange);
    }

    [Test]
    public async Task Decide_NeitherSocNorPosition_NotWorthwhile()
    {
        var decider = NewDecider();

        var decision = decider.Decide(T1, Tlm(soc: null, lat: null, lon: null, speed: 30));

        await Assert.That(decision).IsEqualTo(SyncDecision.SkipNotWorthwhile);
    }

    [Test]
    public async Task Decide_PositionOnly_IsWorthwhile()
    {
        var decider = NewDecider();

        var decision = decider.Decide(T1, Tlm(soc: null, lat: 50.0, lon: 4.0));

        await Assert.That(decision).IsEqualTo(SyncDecision.Relay);
    }

    [Test]
    public async Task Decide_NoChangeSinceRelay_Skips()
    {
        var decider = NewDecider();
        var tlm = Tlm(soc: 60);
        decider.Decide(T1, tlm);
        decider.CommitRelay(tlm);

        var decision = decider.Decide(T2, Tlm(soc: 60));

        await Assert.That(decision).IsEqualTo(SyncDecision.SkipNoChange);
    }

    [Test]
    public async Task Decide_SubToleranceDrift_Skips()
    {
        var decider = NewDecider();
        var tlm = Tlm(soc: 60.0);
        decider.Decide(T1, tlm);
        decider.CommitRelay(tlm);

        // 0.3% < 0.5% tolerance and 0.000005 deg < 0.00001 tolerance.
        var decision = decider.Decide(T2, Tlm(soc: 60.3, lat: 50.000005, lon: 4.0));

        await Assert.That(decision).IsEqualTo(SyncDecision.SkipNoChange);
    }

    [Test]
    public async Task Decide_SocBeyondTolerance_Relays()
    {
        var decider = NewDecider();
        var tlm = Tlm(soc: 60.0);
        decider.Decide(T1, tlm);
        decider.CommitRelay(tlm);

        var decision = decider.Decide(T2, Tlm(soc: 61.0));

        await Assert.That(decision).IsEqualTo(SyncDecision.Relay);
    }

    [Test]
    public async Task Decide_BooleanFlip_Relays()
    {
        var decider = NewDecider();
        var tlm = Tlm(soc: 60, charging: false);
        decider.Decide(T1, tlm);
        decider.CommitRelay(tlm);

        var decision = decider.Decide(T2, Tlm(soc: 60, charging: true));

        await Assert.That(decision).IsEqualTo(SyncDecision.Relay);
    }

    [Test]
    public async Task Decide_UnwatchedFieldChange_DoesNotRelay()
    {
        // Odometer/heading/temps are not in the Watched Subset; changing only
        // those must not trigger a Relay.
        var decider = NewDecider();
        var tlm = new AbrpTelemetry { Utc = 1, Soc = 60, Lat = 50, Lon = 4, Odometer = 1000, BattTemp = 20 };
        decider.Decide(T1, tlm);
        decider.CommitRelay(tlm);

        var changedUnwatched = new AbrpTelemetry { Utc = 2, Soc = 60, Lat = 50, Lon = 4, Odometer = 1050, BattTemp = 25 };
        var decision = decider.Decide(T2, changedUnwatched);

        await Assert.That(decision).IsEqualTo(SyncDecision.SkipNoChange);
    }
}
