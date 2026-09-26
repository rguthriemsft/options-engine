namespace OptionsEngine.Infrastructure.Persistence;

/// <summary>Append-only relational summary and immutable JSON payload for a Phase 6 defense evaluation.</summary>
public sealed class DefenseEvaluationEntity
{
    public long DefenseEvaluationEntityId { get; set; }
    public Guid DefenseEvaluationId { get; set; }
    public long OpenShortCallPositionId { get; set; }
    public Guid HoldingId { get; set; }
    public required string Symbol { get; set; }
    public required string OptionSymbol { get; set; }
    public DateTimeOffset DefenseEvaluationTimestampUtc { get; set; }
    public DateTimeOffset CalculatedAtUtc { get; set; }
    public double? Drs { get; set; }
    public string? DrsClassification { get; set; }
    public required string ProfitTakingSignal { get; set; }
    public required string HardDefenseStatus { get; set; }
    public bool RollEngineRequired { get; set; }
    public Guid? RollEvaluationId { get; set; }
    public required string Disposition { get; set; }
    public double? CurrentCcos { get; set; }
    public int ConfigurationVersion { get; set; }
    public required string DefenseStrategyVersion { get; set; }
    public required string RollStrategyVersion { get; set; }
    public required string EvaluationJson { get; set; }
}

/// <summary>Conditional append-only relational summary and immutable JSON payload for Phase 6 roll analysis.</summary>
public sealed class RollEvaluationEntity
{
    public long RollEvaluationEntityId { get; set; }
    public Guid RollEvaluationId { get; set; }
    public Guid DefenseEvaluationId { get; set; }
    public DateTimeOffset DefenseEvaluationTimestampUtc { get; set; }
    public DateTimeOffset CalculatedAtUtc { get; set; }
    public double? CurrentCcos { get; set; }
    public int RankableCandidateCount { get; set; }
    public int InsufficientCandidateCount { get; set; }
    public string? PreferredOptionSymbol { get; set; }
    public decimal? PreferredStrike { get; set; }
    public DateOnly? PreferredExpiration { get; set; }
    public double? PreferredRqs { get; set; }
    public int ConfigurationVersion { get; set; }
    public required string RollStrategyVersion { get; set; }
    public required string EvaluationJson { get; set; }
}
