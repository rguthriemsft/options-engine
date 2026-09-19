using OptionsEngine.Strategy.PositionSizing;

namespace OptionsEngine.Application.PositionSizing;

/// <summary>
/// Persists the exact immutable bundle assembled by Phase 5E. It intentionally performs no additional source,
/// Holding, market-data, or open-position reads after orchestration completes.
/// </summary>
public sealed class PositionSizingEvaluationPersistenceService(
    IPositionSizingEvaluationOrchestrator orchestrator,
    IPositionSizingEvaluationRepository repository,
    TimeProvider timeProvider) : IPositionSizingEvaluationWriter
{
    public async Task<PositionSizingEvaluation> CreateAsync(
        Guid entryStrategyEvaluationId,
        DateTimeOffset sizingTimestampUtc,
        CancellationToken cancellationToken = default)
    {
        var bundle = await orchestrator.EvaluateAsync(entryStrategyEvaluationId, sizingTimestampUtc, cancellationToken);
        if (bundle.SourceEvaluation is null)
            throw new PositionSizingSourceInconsistencyException(
                "Position Sizing orchestration must provide the immutable source entry strategy evaluation.");
        var actualSourceId = bundle.SourceEvaluation.Evaluation.EntryStrategyEvaluationId;
        if (actualSourceId != entryStrategyEvaluationId)
            throw new PositionSizingSourceInconsistencyException(
                "Position Sizing orchestration returned a source entry strategy evaluation that does not match the requested ID.");
        var input = bundle.Input;
        ArgumentNullException.ThrowIfNull(input.Configuration);
        ArgumentNullException.ThrowIfNull(input.StrategyVersion);

        var evaluation = new PositionSizingEvaluation(
            Guid.NewGuid(),
            actualSourceId,
            timeProvider.GetUtcNow(),
            input.SizingTimestampUtc,
            input.Holding,
            input.ExistingShortCallExposure,
            input.ExistingShortCallDeltaObservations,
            input.PortfolioConcentration,
            input.Configuration,
            input.ConfigurationVersion,
            input.StrategyVersion,
            bundle.Result);
        await repository.InsertAsync(evaluation, cancellationToken);
        return evaluation;
    }
}
