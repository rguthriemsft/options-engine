namespace OptionsEngine.Infrastructure.Persistence;

/// <summary>Append-only relational summary and immutable structured payload for a Phase 4 evaluation.</summary>
public sealed class EntryStrategyEvaluationEntity
{
    public Guid EntryStrategyEvaluationId { get; set; }
    public Guid HoldingId { get; set; }
    public required string Symbol { get; set; }
    public DateOnly IndicatorAsOfDate { get; set; }
    public DateTimeOffset EvaluationTimestampUtc { get; set; }
    public DateTimeOffset CalculatedAtUtc { get; set; }
    public required string IndicatorCalculationVersion { get; set; }
    public int ConfigurationVersion { get; set; }
    public required string StrategyVersion { get; set; }
    public required string CcosStatus { get; set; }
    public double? CcosScore { get; set; }
    public string? CcosClassification { get; set; }
    public bool EntryCandidateExists { get; set; }
    public string? PreferredInitialOptionSymbol { get; set; }
    public decimal? PreferredInitialStrike { get; set; }
    public DateOnly? PreferredInitialExpiration { get; set; }
    public decimal? PreferredInitialReferencePremium { get; set; }
    public required string DispositionReason { get; set; }
    public required string EvaluationJson { get; set; }
}
