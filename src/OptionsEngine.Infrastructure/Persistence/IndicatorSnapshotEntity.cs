namespace OptionsEngine.Infrastructure.Persistence;

/// <summary>Canonical identity and queryable IV30 alongside the complete versioned indicator document.</summary>
public sealed class IndicatorSnapshotEntity
{
    public long IndicatorSnapshotId { get; set; }
    public required string Symbol { get; set; }
    public DateOnly AsOfDate { get; set; }
    public required string IndicatorCalculationVersion { get; set; }
    public int ConfigurationVersion { get; set; }
    public DateTimeOffset CalculatedAt { get; set; }
    public double? Iv30Value { get; set; }
    public required string SnapshotJson { get; set; }
}
