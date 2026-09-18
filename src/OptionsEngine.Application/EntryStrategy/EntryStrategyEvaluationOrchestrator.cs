using OptionsEngine.Application.Indicators;
using OptionsEngine.Domain.Accounts;
using OptionsEngine.MarketData;
using OptionsEngine.MarketData.Models;
using OptionsEngine.Strategy.EntryStrategy;
using OptionsEngine.Strategy.Indicators;

namespace OptionsEngine.Application.EntryStrategy;

/// <summary>Assembles persisted normalized facts into a reproducible input bundle and invokes the pure Phase 4 strategy.</summary>
public sealed class EntryStrategyEvaluationOrchestrator(
    IHoldingRepository holdingRepository,
    IIndicatorDataRepository indicatorRepository,
    IndicatorOrchestrationService indicatorOrchestration,
    IMarketDataProvider marketDataProvider,
    IEarningsDateSource earningsDateSource,
    EntryStrategyOrchestrationConfiguration configuration)
{
    private readonly ContractStrategyCalculator _strategy = new();

    public async Task<EntryStrategyEvaluationBundle> EvaluateAsync(Guid holdingId, DateTimeOffset evaluationTimestampUtc,
        CancellationToken cancellationToken = default)
    {
        if (evaluationTimestampUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Evaluation timestamp must be UTC.", nameof(evaluationTimestampUtc));
        configuration.Validate();

        var holding = await holdingRepository.GetByIdAsync(holdingId, cancellationToken)
            ?? throw new HoldingNotFoundException(holdingId);
        if (!holding.IsEnabled) throw new HoldingDisabledException(holdingId);

        var holdingContext = MapHolding(holding);
        var requestBoundary = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(evaluationTimestampUtc,
            TimeZoneInfo.FindSystemTimeZoneById("America/New_York")).DateTime);
        var provider = marketDataProvider.ProviderName;
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        var indicatorAsOfDate = await indicatorOrchestration.ResolveLatestAsOfDateAsync(holding.Symbol, requestBoundary, cancellationToken)
            ?? throw new IndicatorDataUnavailableException(holding.Symbol);
        var snapshot = await indicatorOrchestration.GetSnapshotAsync(holding.Symbol, indicatorAsOfDate,
            configuration.IndicatorCalculationVersion, configuration.StrategyConfiguration.Version, cancellationToken);
        if (snapshot is null || snapshot.CalculatedAt > evaluationTimestampUtc)
            snapshot = await indicatorOrchestration.CalculateAndPersistAsync(holding.Symbol, indicatorAsOfDate,
                configuration.IndicatorConfiguration, configuration.IndicatorCalculationVersion, evaluationTimestampUtc, cancellationToken);
        if (snapshot.ConfigurationVersion != configuration.StrategyConfiguration.Version)
            throw new InvalidOperationException("The Phase 3 indicator snapshot configuration version does not match the Phase 4 configuration version.");

        var indicatorContext = MapIndicators(snapshot);
        var earnings = await ResolveEarningsAsync(holding, evaluationTimestampUtc, cancellationToken);
        var context = new EvaluationContext(holdingContext, indicatorContext, earnings, indicatorAsOfDate,
            evaluationTimestampUtc, configuration.StrategyConfiguration, configuration.StrategyVersion);
        var observations = await indicatorRepository.GetOptionChainsThroughAsync(holding.Symbol, provider, requestBoundary,
            evaluationTimestampUtc, cancellationToken);
        var selectedChains = OptionChainSnapshotSelector.Select(observations, evaluationTimestampUtc);
        var contracts = selectedChains.SelectMany(x => x.Contracts).Select(MapContract).ToArray();
        var strategyResult = _strategy.Evaluate(context, contracts);
        return new EntryStrategyEvaluationBundle(context, selectedChains, contracts, strategyResult);
    }

    private async Task<EarningsContext> ResolveEarningsAsync(Holding holding, DateTimeOffset evaluationTimestampUtc,
        CancellationToken cancellationToken)
    {
        if (holding.AssetType == AssetType.ExchangeTradedFund)
            return new EarningsContext(AvailabilityStatus.NotApplicable, null);

        var date = await earningsDateSource.GetNextEarningsDateAsync(holding.Symbol, evaluationTimestampUtc, cancellationToken);
        var evaluationDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(evaluationTimestampUtc,
            TimeZoneInfo.FindSystemTimeZoneById("America/New_York")).DateTime);
        return date is not null && date >= evaluationDate
            ? new EarningsContext(AvailabilityStatus.Available, date)
            : new EarningsContext(AvailabilityStatus.Unavailable, null);
    }

    private static HoldingContext MapHolding(Holding holding) => new(holding.HoldingId, holding.Symbol, holding.AssetType,
        holding.AssignmentSensitivity, holding.TaxSensitivity, holding.MaximumInitialDelta, holding.PreferredDeltaMinimum,
        holding.PreferredDeltaMaximum, holding.MinimumCcos, holding.MinimumContractScore, holding.MinimumPremium,
        holding.MinimumAnnualizedYield);

    private static IndicatorContext MapIndicators(IndicatorSnapshot snapshot) => new(snapshot.Symbol, snapshot.AsOfDate,
        snapshot.Iv30, snapshot.IvPercentile, snapshot.RealizedVolatility30, snapshot.Rsi14, snapshot.BollingerPercentB,
        snapshot.BollingerBandwidth, snapshot.Sma20, snapshot.Sma50, snapshot.Sma200, snapshot.MacdHistogram,
        snapshot.ResistancePrice, snapshot.DistanceToResistancePercent, snapshot.ResistanceTouchCount,
        snapshot.ResistanceAgeTradingDays, snapshot.ResistanceUnavailableReason, snapshot.MarketRegime, snapshot.SectorRegime,
        snapshot.IndicatorCalculationVersion, snapshot.ConfigurationVersion, snapshot.CalculatedAt)
    {
        IvRank = snapshot.IvRank
    };

    private static OptionContractContext MapContract(OptionContractSnapshot contract) => new(contract.OptionSymbol,
        contract.UnderlyingSymbol, contract.Timestamp, contract.Expiration, contract.Strike,
        contract.OptionType == OptionType.Call ? OptionContractType.Call : OptionContractType.Put, contract.Bid, contract.Ask,
        contract.OpenInterest, contract.ImpliedVolatility, contract.Delta, contract.Theta, contract.UnderlyingPrice,
        contract.Last, contract.Volume, contract.Provider);
}
