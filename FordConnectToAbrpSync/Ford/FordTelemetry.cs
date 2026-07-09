namespace FordConnectToAbrpSync.Ford;

/// <summary>
/// A slim view of the Ford <c>/v1/telemetry</c> response — only the metrics this
/// worker maps onward. The wire response carries hundreds more; they are ignored.
/// Property names bind to the camelCase JSON via the source-gen naming policy.
/// </summary>
internal sealed record FordTelemetryResponse
{
    /// <summary>Root report time of the snapshot. Used for the stale short-circuit.</summary>
    public DateTimeOffset? UpdateTime { get; init; }

    public FordMetrics? Metrics { get; init; }
}

internal sealed record FordMetrics
{
    public ScalarMetric? XevBatteryStateOfCharge { get; init; }
    public ScalarMetric? XevBatteryEnergyRemaining { get; init; }
    public ScalarMetric? XevBatteryVoltage { get; init; }
    public ScalarMetric? XevBatteryIoCurrent { get; init; }
    public ScalarMetric? XevBatteryCapacity { get; init; }
    public ScalarMetric? XevBatteryRange { get; init; }
    public ScalarMetric? XevBatteryTemperature { get; init; }
    public ScalarMetric? OutsideTemperature { get; init; }
    public ScalarMetric? Speed { get; init; }
    public ScalarMetric? Odometer { get; init; }
    public PositionMetric? Position { get; init; }
    public HeadingMetric? Heading { get; init; }
    public ChargeDisplayMetric? XevBatteryChargeDisplayStatus { get; init; }
    public StringMetric? XevChargeStationPowerType { get; init; }
    public StringMetric? GearLeverPosition { get; init; }
}

internal sealed record ScalarMetric
{
    public double? Value { get; init; }
}

internal sealed record StringMetric
{
    public string? Value { get; init; }
}

internal sealed record PositionMetric
{
    public PositionValue? Value { get; init; }
}

internal sealed record PositionValue
{
    public LocationValue? Location { get; init; }
}

internal sealed record LocationValue
{
    public double? Lat { get; init; }
    public double? Lon { get; init; }
    public double? Alt { get; init; }
}

internal sealed record HeadingMetric
{
    public HeadingValue? Value { get; init; }
}

internal sealed record HeadingValue
{
    public double? Heading { get; init; }
}

internal sealed record ChargeDisplayMetric
{
    /// <summary>Charge state enum, e.g. IN_PROGRESS, NOT_PLUGGED_IN, COMPLETED.</summary>
    public string? Value { get; init; }

    /// <summary>AC | DC | WIRELESS — used as a fallback DC-fast-charge indicator.</summary>
    public string? XevChargerPowerType { get; init; }
}
