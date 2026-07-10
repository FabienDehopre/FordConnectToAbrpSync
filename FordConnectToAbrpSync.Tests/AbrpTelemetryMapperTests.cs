using System.Text.Json;
using FordConnectToAbrpSync.Ford;
using FordConnectToAbrpSync.Serialization;
using FordConnectToAbrpSync.Sync;

namespace FordConnectToAbrpSync.Tests;

public class AbrpTelemetryMapperTests
{
    private static readonly DateTimeOffset SampleTime =
        new(2025, 8, 15, 21, 53, 6, TimeSpan.Zero);

    private static ScalarMetric Scalar(double v) => new() { Value = v };

    private static FordTelemetryResponse FullResponse() => new()
    {
        UpdateTime = SampleTime,
        Metrics = new FordMetrics
        {
            XevBatteryStateOfCharge = Scalar(72.5),
            XevBatteryEnergyRemaining = Scalar(48.2),
            XevBatteryVoltage = Scalar(400),
            XevBatteryIoCurrent = Scalar(50),
            XevBatteryCapacity = Scalar(88),
            XevBatteryRange = Scalar(310),
            XevBatteryTemperature = Scalar(24.5),
            OutsideTemperature = Scalar(18.0),
            Speed = Scalar(95.4),
            Odometer = Scalar(12345),
            Position = new PositionMetric
            {
                Value = new PositionValue
                {
                    Location = new LocationValue { Lat = 50.85, Lon = 4.35, Alt = 120 },
                },
            },
            Heading = new HeadingMetric { Value = new HeadingValue { Heading = 270 } },
            XevBatteryChargeDisplayStatus = new ChargeDisplayMetric { Value = "IN_PROGRESS" },
            XevChargeStationPowerType = new StringMetric { Value = "DC_FAST" },
            GearLeverPosition = new StringMetric { Value = "PARK" },
        },
    };

    [Test]
    public async Task Map_FullSnapshot_MapsEveryField()
    {
        var tlm = AbrpTelemetryMapper.Map(FullResponse());

        await Assert.That(tlm.Utc).IsEqualTo(SampleTime.ToUnixTimeSeconds());
        await Assert.That(tlm.Soc).IsEqualTo(72.5);
        await Assert.That(tlm.Soe).IsEqualTo(48.2);
        await Assert.That(tlm.Speed).IsEqualTo(95.4);
        await Assert.That(tlm.Lat).IsEqualTo(50.85);
        await Assert.That(tlm.Lon).IsEqualTo(4.35);
        await Assert.That(tlm.Elevation).IsEqualTo(120);
        await Assert.That(tlm.Capacity).IsEqualTo(88);
        await Assert.That(tlm.EstBatteryRange).IsEqualTo(310);
        await Assert.That(tlm.ExtTemp).IsEqualTo(18.0);
        await Assert.That(tlm.BattTemp).IsEqualTo(24.5);
        await Assert.That(tlm.Voltage).IsEqualTo(400);
        await Assert.That(tlm.Current).IsEqualTo(50);
        await Assert.That(tlm.Odometer).IsEqualTo(12345);
        await Assert.That(tlm.Heading).IsEqualTo(270);
    }

    [Test]
    public async Task Map_Power_IsVoltageTimesCurrentInKilowatts()
    {
        var tlm = AbrpTelemetryMapper.Map(FullResponse());

        // 400 V * 50 A / 1000 = 20 kW
        await Assert.That(tlm.Power).IsEqualTo(20.0);
    }

    [Test]
    public async Task Map_Power_InvertSign_NegatesValue()
    {
        var tlm = AbrpTelemetryMapper.Map(FullResponse(), invertPowerSign: true);

        await Assert.That(tlm.Power).IsEqualTo(-20.0);
    }

    [Test]
    public async Task Map_Power_MissingVoltageOrCurrent_IsNull()
    {
        var response = new FordTelemetryResponse
        {
            UpdateTime = SampleTime,
            Metrics = new FordMetrics { XevBatteryVoltage = Scalar(400) },
        };

        var tlm = AbrpTelemetryMapper.Map(response);

        await Assert.That(tlm.Power).IsNull();
    }

    [Test]
    [Arguments("IN_PROGRESS", true)]
    [Arguments("NOT_PLUGGED_IN", false)]
    [Arguments("COMPLETED", false)]
    [Arguments("PAUSED", false)]
    public async Task Map_IsCharging_TrueOnlyWhenInProgress(string status, bool expected)
    {
        var response = new FordTelemetryResponse
        {
            UpdateTime = SampleTime,
            Metrics = new FordMetrics
            {
                XevBatteryChargeDisplayStatus = new ChargeDisplayMetric { Value = status },
            },
        };

        var tlm = AbrpTelemetryMapper.Map(response);

        await Assert.That(tlm.IsCharging).IsEqualTo(expected);
    }

    [Test]
    [Arguments("DC_FAST", true)]
    [Arguments("AC_BASIC", false)]
    [Arguments("AC_SMART", false)]
    [Arguments("WIRELESS", false)]
    public async Task Map_IsDcfc_FromStationPowerType(string stationPower, bool expected)
    {
        var response = new FordTelemetryResponse
        {
            UpdateTime = SampleTime,
            Metrics = new FordMetrics
            {
                XevChargeStationPowerType = new StringMetric { Value = stationPower },
            },
        };

        var tlm = AbrpTelemetryMapper.Map(response);

        await Assert.That(tlm.IsDcfc).IsEqualTo(expected);
    }

    [Test]
    public async Task Map_IsDcfc_FallsBackToChargerPowerType_WhenStationPowerMissing()
    {
        var response = new FordTelemetryResponse
        {
            UpdateTime = SampleTime,
            Metrics = new FordMetrics
            {
                XevBatteryChargeDisplayStatus =
                    new ChargeDisplayMetric { Value = "IN_PROGRESS", XevChargerPowerType = "DC" },
            },
        };

        var tlm = AbrpTelemetryMapper.Map(response);

        await Assert.That(tlm.IsDcfc).IsEqualTo(true);
    }

    [Test]
    [Arguments("PARK", true)]
    [Arguments("DRIVE", false)]
    [Arguments("REVERSE", false)]
    public async Task Map_IsParked_TrueOnlyWhenGearPark(string gear, bool expected)
    {
        var response = new FordTelemetryResponse
        {
            UpdateTime = SampleTime,
            Metrics = new FordMetrics { GearLeverPosition = new StringMetric { Value = gear } },
        };

        var tlm = AbrpTelemetryMapper.Map(response);

        await Assert.That(tlm.IsParked).IsEqualTo(expected);
    }

    [Test]
    [Arguments(0.0, null)]
    [Arguments(120.0, 120.0)]
    [Arguments(-5.0, -5.0)]
    public async Task Map_Elevation_TreatsZeroAltAsAbsent(double alt, double? expected)
    {
        var response = new FordTelemetryResponse
        {
            UpdateTime = SampleTime,
            Metrics = new FordMetrics
            {
                Position = new PositionMetric
                {
                    Value = new PositionValue
                    {
                        Location = new LocationValue { Lat = 50.85, Lon = 4.35, Alt = alt },
                    },
                },
            },
        };

        var tlm = AbrpTelemetryMapper.Map(response);

        await Assert.That(tlm.Elevation).IsEqualTo(expected);
    }

    [Test]
    public async Task Map_EmptyMetrics_LeavesOnlyUtc()
    {
        var response = new FordTelemetryResponse { UpdateTime = SampleTime };

        var tlm = AbrpTelemetryMapper.Map(response);

        await Assert.That(tlm.Utc).IsEqualTo(SampleTime.ToUnixTimeSeconds());
        await Assert.That(tlm.Soc).IsNull();
        await Assert.That(tlm.Power).IsNull();
        await Assert.That(tlm.IsCharging).IsNull();
        await Assert.That(tlm.IsParked).IsNull();
    }

    [Test]
    public async Task Serialize_OmitsNullFields_AndUsesSnakeCaseNames()
    {
        var response = new FordTelemetryResponse
        {
            UpdateTime = SampleTime,
            Metrics = new FordMetrics
            {
                XevBatteryStateOfCharge = Scalar(72.5),
                XevBatteryChargeDisplayStatus = new ChargeDisplayMetric { Value = "IN_PROGRESS" },
            },
        };
        var tlm = AbrpTelemetryMapper.Map(response);

        var json = JsonSerializer.Serialize(tlm, AppJsonSerializerContext.Default.AbrpTelemetry);

        await Assert.That(json).Contains("\"utc\":");
        await Assert.That(json).Contains("\"soc\":72.5");
        await Assert.That(json).Contains("\"is_charging\":true");
        // Absent metrics must not appear at all.
        await Assert.That(json).DoesNotContain("power");
        await Assert.That(json).DoesNotContain("speed");
        await Assert.That(json).DoesNotContain("est_battery_range");
    }

    [Test]
    public async Task Deserialize_RealFordPayloadShape_BindsCamelCaseMetrics()
    {
        // Mirrors the nesting of the real Ford /v1/telemetry response.
        const string json = """
        {
          "updateTime": "2025-08-15T21:53:06.41924Z",
          "metrics": {
            "xevBatteryStateOfCharge": { "value": 64.0 },
            "speed": { "value": 42.0 },
            "position": { "value": { "location": { "lat": 50.85, "lon": 4.35, "alt": 100 } } },
            "gearLeverPosition": { "value": "DRIVE" },
            "xevBatteryChargeDisplayStatus": { "value": "NOT_PLUGGED_IN", "xevChargerPowerType": "AC" }
          }
        }
        """;

        var response = JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.FordTelemetryResponse);

        await Assert.That(response).IsNotNull();
        var tlm = AbrpTelemetryMapper.Map(response!);
        await Assert.That(tlm.Soc).IsEqualTo(64.0);
        await Assert.That(tlm.Speed).IsEqualTo(42.0);
        await Assert.That(tlm.Lat).IsEqualTo(50.85);
        await Assert.That(tlm.IsParked).IsEqualTo(false);
        await Assert.That(tlm.IsCharging).IsEqualTo(false);
    }
}
