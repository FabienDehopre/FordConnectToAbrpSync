using System.Text.Json.Serialization;

namespace FordConnectToAbrpSync.Abrp;

/// <summary>
/// The ABRP telemetry payload (the <c>tlm</c> object of the /tlm/send endpoint).
/// Null members are omitted at serialization time so ABRP receives absent fields
/// rather than nulls/zeros, per its API contract.
/// </summary>
internal sealed record AbrpTelemetry
{
    /// <summary>UTC timestamp of the reading, epoch seconds. Always present.</summary>
    [JsonPropertyName("utc")]
    public required long Utc { get; init; }

    [JsonPropertyName("soc")]
    public double? Soc { get; init; }

    [JsonPropertyName("soe")]
    public double? Soe { get; init; }

    [JsonPropertyName("power")]
    public double? Power { get; init; }

    [JsonPropertyName("speed")]
    public double? Speed { get; init; }

    [JsonPropertyName("lat")]
    public double? Lat { get; init; }

    [JsonPropertyName("lon")]
    public double? Lon { get; init; }

    [JsonPropertyName("elevation")]
    public double? Elevation { get; init; }

    [JsonPropertyName("is_charging")]
    public bool? IsCharging { get; init; }

    [JsonPropertyName("is_dcfc")]
    public bool? IsDcfc { get; init; }

    [JsonPropertyName("is_parked")]
    public bool? IsParked { get; init; }

    [JsonPropertyName("capacity")]
    public double? Capacity { get; init; }

    [JsonPropertyName("est_battery_range")]
    public double? EstBatteryRange { get; init; }

    [JsonPropertyName("ext_temp")]
    public double? ExtTemp { get; init; }

    [JsonPropertyName("batt_temp")]
    public double? BattTemp { get; init; }

    [JsonPropertyName("voltage")]
    public double? Voltage { get; init; }

    [JsonPropertyName("current")]
    public double? Current { get; init; }

    [JsonPropertyName("odometer")]
    public double? Odometer { get; init; }

    [JsonPropertyName("heading")]
    public double? Heading { get; init; }
}
