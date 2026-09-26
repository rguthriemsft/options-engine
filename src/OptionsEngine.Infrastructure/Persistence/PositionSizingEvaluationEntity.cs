namespace OptionsEngine.Infrastructure.Persistence;

/// <summary>Append-only relational summary and immutable structured payload for a Position Sizing evaluation.</summary>
public sealed class PositionSizingEvaluationEntity
{
    public long PositionSizingEvaluationEntityId { get; set; }
    public Guid PositionSizingEvaluationId { get; set; }
    public Guid EntryStrategyEvaluationId { get; set; }
    public Guid? HoldingId { get; set; }
    public string? Symbol { get; set; }
    public DateTimeOffset CalculatedAtUtc { get; set; }
    public DateTimeOffset SizingTimestampUtc { get; set; }
    public required string Status { get; set; }
    public int ConfigurationVersion { get; set; }
    public required string PositionSizingStrategyVersion { get; set; }
    public int? DesiredTotalContracts { get; set; }
    public int? AdditionalContracts { get; set; }
    public int? ResultingTotalContracts { get; set; }
    public double? ExistingDer { get; set; }
    public double? MaximumDer { get; set; }
    public required string EvaluationJson { get; set; }
}

/// <summary>Narrow mutable current-state open short-call position; not a transaction or campaign ledger.</summary>
public sealed class OpenShortCallPositionEntity
{
    public long OpenShortCallPositionId { get; set; }
    public Guid HoldingId { get; set; }
    public required string OptionSymbol { get; set; }
    public int Contracts { get; set; }
    public decimal Strike { get; set; }
    public DateOnly Expiration { get; set; }
    public decimal? OpeningPremiumPerShare { get; set; }
    public DateTimeOffset? OpenedAtUtc { get; set; }
}
