using FordConnectToAbrpSync.Abrp;
using FordConnectToAbrpSync.Ford;

namespace FordConnectToAbrpSync.Sync;

/// <summary>
/// Maps a Ford <see cref="FordTelemetryResponse"/> (a Telemetry Snapshot) to the
/// ABRP <see cref="AbrpTelemetry"/> payload. Absent Ford metrics map to null,
/// which the serializer omits.
/// </summary>
internal static class AbrpTelemetryMapper
{
    private const string ChargingInProgress = "IN_PROGRESS";
    private const string DcFastStationPower = "DC_FAST";
    private const string DcChargerPower = "DC";
    private const string GearPark = "PARK";

    /// <param name="invertPowerSign">
    /// ABRP expects power positive on discharge, negative on charge. If the Ford
    /// current sign is the opposite convention, set this to flip it.
    /// </param>
    public static AbrpTelemetry Map(FordTelemetryResponse response, bool invertPowerSign = false)
    {
        var m = response.Metrics;
        var utc = response.UpdateTime?.ToUnixTimeSeconds()
                  ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        if (m is null)
        {
            return new AbrpTelemetry { Utc = utc };
        }

        return new AbrpTelemetry
        {
            Utc = utc,
            Soc = m.XevBatteryStateOfCharge?.Value,
            Soe = m.XevBatteryEnergyRemaining?.Value,
            Power = ComputePower(m, invertPowerSign),
            Speed = m.Speed?.Value,
            Lat = m.Position?.Value?.Location?.Lat,
            Lon = m.Position?.Value?.Location?.Lon,
            Elevation = m.Position?.Value?.Location?.Alt,
            IsCharging = MapIsCharging(m),
            IsDcfc = MapIsDcfc(m),
            IsParked = MapIsParked(m),
            Capacity = m.XevBatteryCapacity?.Value,
            EstBatteryRange = m.XevBatteryRange?.Value,
            ExtTemp = m.OutsideTemperature?.Value,
            BattTemp = m.XevBatteryTemperature?.Value,
            Voltage = m.XevBatteryVoltage?.Value,
            Current = m.XevBatteryIoCurrent?.Value,
            Odometer = m.Odometer?.Value,
            Heading = m.Heading?.Value?.Heading,
        };
    }

    private static double? ComputePower(FordMetrics m, bool invert)
    {
        var v = m.XevBatteryVoltage?.Value;
        var i = m.XevBatteryIoCurrent?.Value;
        if (v is null || i is null)
        {
            return null;
        }

        var kw = v.Value * i.Value / 1000.0;
        return invert ? -kw : kw;
    }

    private static bool? MapIsCharging(FordMetrics m)
    {
        var status = m.XevBatteryChargeDisplayStatus?.Value;
        return status is null
            ? null
            : string.Equals(status, ChargingInProgress, StringComparison.OrdinalIgnoreCase);
    }

    private static bool? MapIsDcfc(FordMetrics m)
    {
        var stationPower = m.XevChargeStationPowerType?.Value;
        if (stationPower is not null)
        {
            return string.Equals(stationPower, DcFastStationPower, StringComparison.OrdinalIgnoreCase);
        }

        var chargerPower = m.XevBatteryChargeDisplayStatus?.XevChargerPowerType;
        return chargerPower is null
            ? null
            : string.Equals(chargerPower, DcChargerPower, StringComparison.OrdinalIgnoreCase);
    }

    private static bool? MapIsParked(FordMetrics m)
    {
        var gear = m.GearLeverPosition?.Value;
        return gear is null
            ? null
            : string.Equals(gear, GearPark, StringComparison.OrdinalIgnoreCase);
    }
}
