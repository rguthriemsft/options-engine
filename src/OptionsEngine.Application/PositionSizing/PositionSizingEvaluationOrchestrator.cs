using System.Collections.Immutable;
using OptionsEngine.Application.EntryStrategy;
using OptionsEngine.Domain.Accounts;
using OptionsEngine.Strategy.EntryStrategy;
using OptionsEngine.Strategy.PositionSizing;

namespace OptionsEngine.Application.PositionSizing;

/// <summary>Assembles persisted and current-state facts before invoking the pure Phase 5 Strategy engine.</summary>
public sealed class PositionSizingEvaluationOrchestrator(
    IEntryStrategyEvaluationRepository entryStrategyEvaluationRepository,
    IHoldingRepository holdingRepository,
    IPositionSizingHoldingRepository accountHoldingRepository,
    IPositionSizingMarketDataRepository marketDataRepository,
    IOpenShortCallPositionRepository openShortCallPositionRepository,
    IPositionSizingEngine strategy,
    PositionSizingOrchestrationConfiguration configuration) : IPositionSizingEvaluationOrchestrator
{
    public async Task<PositionSizingEvaluationBundle> EvaluateAsync(
        Guid entryStrategyEvaluationId,
        DateTimeOffset sizingTimestampUtc,
        CancellationToken cancellationToken = default)
    {
        if (sizingTimestampUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Sizing timestamp must be UTC.", nameof(sizingTimestampUtc));
        configuration.Validate();

        var source = await entryStrategyEvaluationRepository.GetByIdAsync(entryStrategyEvaluationId, cancellationToken)
            ?? throw new EntryStrategyEvaluationNotFoundException(entryStrategyEvaluationId);
        if (!source.Evaluation.EntryCandidateExists)
            return EvaluateNotApplicable(source, sizingTimestampUtc);

        var sourceHolding = source.Evaluation.Context.Holding;
        var holding = await holdingRepository.GetByIdAsync(sourceHolding.HoldingId, cancellationToken)
            ?? throw new HoldingNotFoundException(sourceHolding.HoldingId);
        var holdingContext = MapHolding(holding);
        var provider = GetPreferredContractProvider(source.Evaluation);

        var accountHoldings = await accountHoldingRepository.GetByAccountIdAsync(holding.AccountId, cancellationToken)
            ?? throw new PositionSizingSourceInconsistencyException("The account Holding query returned no result.");
        var concentration = await BuildConcentrationContextAsync(
            accountHoldings, holding, source.Evaluation.Context.IndicatorAsOfDate, provider, cancellationToken);

        var exposures = await openShortCallPositionRepository.GetOpenShortCallsByHoldingAsync(
            holding.HoldingId, cancellationToken)
            ?? throw new PositionSizingSourceInconsistencyException("The open short-call query returned no result.");
        var deltaObservations = await BuildDeltaObservationsAsync(
            exposures, provider, sizingTimestampUtc, cancellationToken);

        var input = new PositionSizingInput(
            source.Evaluation,
            holdingContext,
            [.. exposures.Select(NormalizeExposure)],
            deltaObservations,
            concentration,
            sizingTimestampUtc,
            configuration.PositionSizingConfiguration,
            configuration.PositionSizingConfiguration.Version,
            configuration.StrategyVersion);
        return new PositionSizingEvaluationBundle(source, input, strategy.Evaluate(input));
    }

    private PositionSizingEvaluationBundle EvaluateNotApplicable(
        PersistedEntryStrategyEvaluation source,
        DateTimeOffset sizingTimestampUtc)
    {
        var input = new PositionSizingInput(
            source.Evaluation,
            null,
            [],
            [],
            null,
            sizingTimestampUtc,
            configuration.PositionSizingConfiguration,
            configuration.PositionSizingConfiguration.Version,
            configuration.StrategyVersion);
        return new PositionSizingEvaluationBundle(source, input, strategy.Evaluate(input));
    }

    private async Task<PortfolioConcentrationContext> BuildConcentrationContextAsync(
        IReadOnlyList<Holding> accountHoldings,
        Holding targetHolding,
        DateOnly indicatorAsOfDate,
        string provider,
        CancellationToken cancellationToken)
    {
        var targetCount = accountHoldings.Count(holding => holding.HoldingId == targetHolding.HoldingId);
        if (targetCount != 1 || accountHoldings.Any(holding => holding.AccountId != targetHolding.AccountId))
            throw new PositionSizingSourceInconsistencyException(
                "The account Holding query must contain the target Holding exactly once and no other Account.");

        var concentrationHoldings = new List<PortfolioConcentrationHolding>(accountHoldings.Count);
        foreach (var accountHolding in accountHoldings)
        {
            var price = await marketDataRepository.GetLatestDailyCloseAsync(
                accountHolding.Symbol, provider, indicatorAsOfDate, cancellationToken);
            concentrationHoldings.Add(new PortfolioConcentrationHolding(
                accountHolding.HoldingId,
                accountHolding.AccountId,
                accountHolding.Symbol,
                accountHolding.HoldingId == targetHolding.HoldingId ? targetHolding.Shares : accountHolding.Shares,
                price?.Close,
                price?.Date));
        }

        return new PortfolioConcentrationContext(
            targetHolding.HoldingId,
            targetHolding.AccountId,
            indicatorAsOfDate,
            [.. concentrationHoldings]);
    }

    private async Task<ImmutableArray<ExistingShortCallDeltaObservation>> BuildDeltaObservationsAsync(
        IReadOnlyList<ExistingShortCallExposure> exposures,
        string provider,
        DateTimeOffset sizingTimestampUtc,
        CancellationToken cancellationToken)
    {
        var symbols = exposures.Where(exposure => exposure.Contracts > 0)
            .Select(exposure => NormalizeOptionSymbol(exposure.OptionSymbol))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (symbols.Length == 0)
            return [];

        var observations = await marketDataRepository.GetLatestOptionDeltaObservationsAsync(
            symbols, provider, sizingTimestampUtc, cancellationToken)
            ?? throw new PositionSizingSourceInconsistencyException("The option-observation query returned no result.");
        return [.. observations.Select(observation => new ExistingShortCallDeltaObservation(
            NormalizeOptionSymbol(observation.OptionSymbol), observation.Delta, observation.ObservationTimestampUtc))];
    }

    private static PositionSizingHoldingContext MapHolding(Holding holding) => new(
        holding.HoldingId,
        holding.AccountId,
        holding.Symbol,
        holding.AssetType,
        holding.Shares,
        holding.AssignmentSensitivity,
        holding.TaxSensitivity,
        holding.MaximumCoveragePercent,
        holding.MaximumDeltaExposureRatio);

    private static ExistingShortCallExposure NormalizeExposure(ExistingShortCallExposure exposure) => exposure with
    {
        OptionSymbol = NormalizeOptionSymbol(exposure.OptionSymbol)
    };

    private static string GetPreferredContractProvider(EntryStrategyEvaluation source)
    {
        if (string.IsNullOrWhiteSpace(source.PreferredInitialOptionSymbol))
            throw new PositionSizingSourceInconsistencyException(
                "An entry candidate must identify its preferred initial option symbol.");
        var contracts = source.Contracts ?? throw new PositionSizingSourceInconsistencyException(
            "An entry candidate must retain its persisted Phase 4 contract evaluations.");
        var matches = contracts.Where(contract => string.Equals(
            contract.Contract.OptionSymbol,
            source.PreferredInitialOptionSymbol,
            StringComparison.Ordinal)).ToArray();
        if (matches.Length != 1 || string.IsNullOrWhiteSpace(matches[0].Contract.Provider))
            throw new PositionSizingSourceInconsistencyException(
                "The preferred Phase 4 contract must provide one unambiguous market-data provider identity.");
        return matches[0].Contract.Provider;
    }

    private static string NormalizeOptionSymbol(string optionSymbol) => string.IsNullOrWhiteSpace(optionSymbol)
        ? throw new PositionSizingSourceInconsistencyException("An open short-call option symbol is required.")
        : optionSymbol.Trim().ToUpperInvariant();
}
