using OptionsEngine.Strategy.EntryStrategy;
using OptionsEngine.Strategy.Indicators;
using OptionsEngine.MarketData.Models;

namespace OptionsEngine.Application.EntryStrategy;

/// <summary>Complete immutable Phase 4 evaluation payload, including selected raw chain observations.</summary>
public sealed record PersistedEntryStrategyEvaluation(
    EntryStrategyEvaluation Evaluation,
    IReadOnlyList<OptionChain> SelectedOptionChains,
    IReadOnlyList<OptionContractContext> SelectedContracts);

public sealed record EntryStrategyEvaluationHistoryItem(
    Guid EntryStrategyEvaluationId,
    Guid HoldingId,
    string Symbol,
    DateOnly IndicatorAsOfDate,
    DateTimeOffset EvaluationTimestampUtc,
    DateTimeOffset CalculatedAtUtc,
    ScoreStatus CcosStatus,
    double? CcosScore,
    string? CcosClassification,
    bool EntryCandidateExists,
    string? PreferredInitialOptionSymbol,
    decimal? PreferredInitialStrike,
    DateOnly? PreferredInitialExpiration,
    DispositionReasonCode DispositionReason,
    string IndicatorCalculationVersion,
    ConfigurationVersion ConfigurationVersion,
    string StrategyVersion);

public interface IEntryStrategyEvaluationRepository
{
    Task InsertAsync(PersistedEntryStrategyEvaluation evaluation, CancellationToken cancellationToken = default);
    Task<PersistedEntryStrategyEvaluation?> GetByIdAsync(Guid evaluationId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<EntryStrategyEvaluationHistoryItem>> GetHistoryByHoldingAsync(Guid holdingId,
        CancellationToken cancellationToken = default);
}

/// <summary>Application write boundary used by the HTTP layer without exposing orchestration details.</summary>
public interface IEntryStrategyEvaluationWriter
{
    Task<PersistedEntryStrategyEvaluation> CreateAsync(Guid holdingId, DateTimeOffset evaluationTimestampUtc,
        CancellationToken cancellationToken = default);
}

public sealed class EntryStrategyEvaluationPersistenceService(
    EntryStrategyEvaluationOrchestrator orchestrator,
    IEntryStrategyEvaluationRepository repository,
    TimeProvider timeProvider) : IEntryStrategyEvaluationWriter
{
    public async Task<PersistedEntryStrategyEvaluation> CreateAsync(Guid holdingId, DateTimeOffset evaluationTimestampUtc,
        CancellationToken cancellationToken = default)
    {
        var bundle = await orchestrator.EvaluateAsync(holdingId, evaluationTimestampUtc, cancellationToken);
        var calculatedAtUtc = timeProvider.GetUtcNow();
        var evaluation = new EntryStrategyEvaluation(
            Guid.NewGuid(), calculatedAtUtc, bundle.Context, bundle.StrategyResult.Ccos,
            [bundle.StrategyResult.BreakoutVeto], bundle.StrategyResult.Contracts,
            bundle.StrategyResult.EntryCandidateExists, bundle.StrategyResult.PreferredInitialOptionSymbol,
            bundle.StrategyResult.PreferredInitialStrike, bundle.StrategyResult.PreferredInitialExpiration,
            bundle.StrategyResult.PreferredInitialReferencePremium, bundle.StrategyResult.DispositionReason,
            bundle.StrategyResult.MissingInputs, bundle.StrategyResult.Explanations);
        var persisted = new PersistedEntryStrategyEvaluation(evaluation, bundle.SelectedOptionChains, bundle.SelectedContracts);
        await repository.InsertAsync(persisted, cancellationToken);
        return persisted;
    }
}
