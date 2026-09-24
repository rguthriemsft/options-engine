using OptionsEngine.Application.EntryStrategy;
using OptionsEngine.Application.Indicators;
using OptionsEngine.Domain.Accounts;
using OptionsEngine.Strategy.Defense;
using OptionsEngine.Strategy.EntryStrategy;
using OptionsEngine.Strategy.Indicators;

namespace OptionsEngine.Application.Defense;

/// <summary>Resolves current Phase 3 facts and evaluates CCOS with the existing Phase 4 calculator.</summary>
public sealed class CurrentCcosResolver(
    IndicatorOrchestrationService indicatorOrchestration,
    EntryStrategyOrchestrationConfiguration configuration) : ICurrentCcosResolver
{
    private readonly CcosCalculator calculator = new();

    public async Task<CurrentCcosContext?> ResolveAsync(Holding holding, EarningsContext earnings,
        DateTimeOffset evaluationTimestampUtc, ConfigurationVersion configurationVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(holding);
        ArgumentNullException.ThrowIfNull(earnings);
        if (evaluationTimestampUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Evaluation timestamp must be UTC.", nameof(evaluationTimestampUtc));
        configuration.Validate();
        if (configuration.StrategyConfiguration.Version != configurationVersion)
            throw new InvalidOperationException(
                "The Phase 4 CCOS configuration version must match the Phase 6 configuration version.");

        var requestBoundary = DefenseEvaluator.EvaluationDate(evaluationTimestampUtc);
        var indicatorAsOfDate = await indicatorOrchestration.ResolveLatestAsOfDateAsync(
            holding.Symbol, requestBoundary, cancellationToken);
        if (indicatorAsOfDate is null)
            return null;

        var snapshot = await indicatorOrchestration.GetSnapshotAsync(holding.Symbol, indicatorAsOfDate.Value,
            configuration.IndicatorCalculationVersion, configuration.StrategyConfiguration.Version,
            cancellationToken);
        if (snapshot is null || snapshot.CalculatedAt > evaluationTimestampUtc)
            snapshot = await indicatorOrchestration.CalculateAndPersistAsync(holding.Symbol,
                indicatorAsOfDate.Value, configuration.IndicatorConfiguration,
                configuration.IndicatorCalculationVersion, evaluationTimestampUtc, cancellationToken);
        if (snapshot.ConfigurationVersion != configuration.StrategyConfiguration.Version)
            throw new InvalidOperationException(
                "The Phase 3 indicator snapshot configuration version does not match the Phase 4 configuration version.");

        var context = new EvaluationContext(
            EntryStrategyContextMapper.MapHolding(holding),
            EntryStrategyContextMapper.MapIndicators(snapshot),
            earnings,
            indicatorAsOfDate.Value,
            evaluationTimestampUtc,
            configuration.StrategyConfiguration,
            configuration.StrategyVersion);
        return new CurrentCcosContext(context, calculator.Evaluate(context).Ccos);
    }
}
