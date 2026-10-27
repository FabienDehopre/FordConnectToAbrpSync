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

        var location = m.Position?.Value?.Location;

        return new AbrpTelemetry
        {
            Utc = utc,
            Soc = ValueOf(m.XevBatteryStateOfCharge),
            Soe = ValueOf(m.XevBatteryEnergyRemaining),
            Power = ComputePower(m, invertPowerSign),
            Speed = ValueOf(m.Speed),
            Lat = location?.Lat,
            Lon = location?.Lon,
            Elevation = MapElevation(location),
            IsCharging = MapIsCharging(m),
            IsDcfc = MapIsDcfc(m),
            IsParked = MapIsParked(m),
            Capacity = ValueOf(m.XevBatteryCapacity),
            EstBatteryRange = ValueOf(m.XevBatteryRange),
            ExtTemp = ValueOf(m.OutsideTemperature),
            BattTemp = ValueOf(m.XevBatteryTemperature),
            Voltage = ValueOf(m.XevBatteryVoltage),
            Current = ValueOf(m.XevBatteryIoCurrent),
            Odometer = ValueOf(m.Odometer),
            Heading = m.Heading?.Value?.Heading,
        };
    }

    private static double? ValueOf(ScalarMetric? metric) => metric?.Value;

    // Ford reports alt 0.0 when the GPS altitude is unavailable, not sea level.
    // Treat exactly 0.0 as absent so ABRP doesn't record a bogus elevation.
    private static double? MapElevation(LocationValue? location)
    {
        var alt = location?.Alt;
        return alt is null or 0.0 ? null : alt;
    }

    private static double? ComputePower(FordMetrics m, bool invert)
    {
        var v = ValueOf(m.XevBatteryVoltage);
        var i = ValueOf(m.XevBatteryIoCurrent);
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
