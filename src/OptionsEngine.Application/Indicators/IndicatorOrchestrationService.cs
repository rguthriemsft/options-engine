using OptionsEngine.Application.MarketData;
using OptionsEngine.MarketData;
using OptionsEngine.Strategy.Indicators;

namespace OptionsEngine.Application.Indicators;

public sealed record IndicatorOrchestrationConfiguration
{
    public IReadOnlyDictionary<string, string> SectorBenchmarks { get; init; } = new Dictionary<string, string>();
}

/// <summary>Coordinates persisted normalized inputs and pure Phase 3 calculations; it does not fetch current market data.</summary>
public sealed class IndicatorOrchestrationService(
    IIndicatorDataRepository repository,
    IMarketDataProvider provider,
    IndicatorOrchestrationConfiguration applicationConfiguration)
{
    private readonly CoreTechnicalIndicatorCalculator _technical = new();
    private readonly ImpliedVolatilityContextCalculator _iv = new();
    private readonly ResistanceIndicatorCalculator _resistance = new();
    private readonly RegimeIndicatorCalculator _regime = new();

    public async Task<IndicatorSnapshot> CalculateAndPersistAsync(string symbol, DateOnly asOfDate, IndicatorConfiguration configuration,
        IndicatorCalculationVersion calculationVersion, DateTimeOffset calculatedAt, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(calculationVersion);
        if (calculatedAt.Offset != TimeSpan.Zero) throw new ArgumentException("CalculatedAt must be UTC.", nameof(calculatedAt));
        var normalized = symbol.Trim().ToUpperInvariant();
        var providerName = provider.ProviderName;
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);

        var prices = await repository.GetPricesThroughAsync(normalized, providerName, asOfDate, cancellationToken);
        var marketSymbol = configuration.Regime.MarketBenchmark.Trim().ToUpperInvariant();
        var marketPrices = await repository.GetPricesThroughAsync(marketSymbol, providerName, asOfDate, cancellationToken);
        var sectorSymbol = applicationConfiguration.SectorBenchmarks
            .FirstOrDefault(pair => string.Equals(pair.Key, normalized, StringComparison.OrdinalIgnoreCase)).Value;
        sectorSymbol = string.IsNullOrWhiteSpace(sectorSymbol) ? null : sectorSymbol.Trim().ToUpperInvariant();
        IReadOnlyList<IndicatorPriceObservation> sectorPrices = sectorSymbol is null
            ? [] : await repository.GetPricesThroughAsync(sectorSymbol, providerName, asOfDate, cancellationToken);
        var chains = await repository.GetOptionChainsThroughAsync(normalized, providerName, asOfDate, calculatedAt, cancellationToken);
        var ivHistory = await repository.GetPriorValidIv30Async(normalized, asOfDate, calculationVersion, configuration.Version,
            Math.Max(0, configuration.ImpliedVolatility.HistoricalLookbackValidObservations - 1), cancellationToken);

        var priceRequest = new IndicatorCalculationRequest(normalized, asOfDate, prices, configuration, calculationVersion, calculatedAt);
        var snapshot = _technical.Calculate(priceRequest);
        var iv = _iv.Calculate(new ImpliedVolatilityCalculationRequest(normalized, asOfDate,
            chains.Select(ImpliedVolatilityObservationMapper.Map).ToArray(), ivHistory, configuration, calculationVersion, calculatedAt));
        var resistance = _resistance.Calculate(priceRequest);
        var regime = _regime.Calculate(new RegimeCalculationRequest(normalized, asOfDate, marketPrices, sectorSymbol, sectorPrices,
            configuration, calculationVersion, calculatedAt));
        snapshot = regime.ApplyTo(resistance.ApplyTo(iv.ApplyTo(snapshot)));
        await repository.UpsertSnapshotAsync(snapshot, cancellationToken);
        return snapshot;
    }

    public Task<IndicatorSnapshot?> GetSnapshotAsync(string symbol, DateOnly asOfDate,
        IndicatorCalculationVersion calculationVersion, ConfigurationVersion configurationVersion, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        return repository.GetSnapshotAsync(symbol.Trim().ToUpperInvariant(), asOfDate, calculationVersion, configurationVersion, cancellationToken);
    }
}
