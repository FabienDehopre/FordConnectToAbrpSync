using FordConnectToAbrpSync.Ford;
using FordConnectToAbrpSync.Sync;

namespace FordConnectToAbrpSync.Tests;

public class IdleDetectorTests
{
    private static FordTelemetryResponse Snapshot(string? ignition, string? charge = null) => new()
    {
        Metrics = new FordMetrics
        {
            IgnitionStatus = ignition is null ? null : new StringMetric { Value = ignition },
            XevBatteryChargeDisplayStatus = charge is null
                ? null
                : new ChargeDisplayMetric { Value = charge },
        },
    };

    [Test]
    public async Task IsIdle_IgnitionOffNotCharging_IsIdle()
    {
        await Assert.That(IdleDetector.IsIdle(Snapshot("OFF", "NOT_PLUGGED_IN"))).IsTrue();
    }

    [Test]
    public async Task IsIdle_IgnitionOffNoChargeMetric_IsIdle()
    {
        await Assert.That(IdleDetector.IsIdle(Snapshot("OFF"))).IsTrue();
    }

    [Test]
    public async Task IsIdle_IgnitionOffLowercase_IsIdle()
    {
        await Assert.That(IdleDetector.IsIdle(Snapshot("off"))).IsTrue();
    }

    [Test]
    public async Task IsIdle_IgnitionOffButCharging_IsNotIdle()
    {
        await Assert.That(IdleDetector.IsIdle(Snapshot("OFF", "IN_PROGRESS"))).IsFalse();
    }

    [Test]
    [Arguments("ON")]
    [Arguments("ACCESSORY")]
    [Arguments("UNKNOWN")]
    [Arguments("UNRECOGNIZED")]
    public async Task IsIdle_IgnitionNotOff_IsNotIdle(string ignition)
    {
        await Assert.That(IdleDetector.IsIdle(Snapshot(ignition))).IsFalse();
    }

    [Test]
    public async Task IsIdle_IgnitionMetricAbsent_IsNotIdle()
    {
        await Assert.That(IdleDetector.IsIdle(Snapshot(null))).IsFalse();
    }

    [Test]
    public async Task IsIdle_NoMetrics_IsNotIdle()
    {
        await Assert.That(IdleDetector.IsIdle(new FordTelemetryResponse())).IsFalse();
    }
}
