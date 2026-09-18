namespace OptionsEngine.Strategy.Indicators;

public enum MarketRegime
{
    Bullish,
    Bearish,
    Neutral,
    InsufficientData
}

/// <summary>The application supplies the resolved sector benchmark; Strategy never infers an ETF mapping.</summary>
public sealed record RegimeCalculationRequest(
    string UnderlyingSymbol,
    DateOnly AsOfDate,
    IReadOnlyList<IndicatorPriceObservation> MarketObservations,
    string? SectorBenchmarkSymbol,
    IReadOnlyList<IndicatorPriceObservation> SectorObservations,
    IndicatorConfiguration Configuration,
    IndicatorCalculationVersion IndicatorCalculationVersion,
    DateTimeOffset CalculatedAt);

public sealed record RegimeContext(
    string UnderlyingSymbol,
    DateOnly AsOfDate,
    MarketRegime MarketRegime,
    MarketRegime SectorRegime,
    IndicatorCalculationVersion IndicatorCalculationVersion,
    ConfigurationVersion ConfigurationVersion,
    DateTimeOffset CalculatedAt)
{
    public IndicatorSnapshot ApplyTo(IndicatorSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!string.Equals(snapshot.Symbol, UnderlyingSymbol, StringComparison.OrdinalIgnoreCase) || snapshot.AsOfDate != AsOfDate ||
            snapshot.IndicatorCalculationVersion != IndicatorCalculationVersion || snapshot.ConfigurationVersion != ConfigurationVersion)
            throw new ArgumentException("Regime context and indicator snapshot identity must match.", nameof(snapshot));
        return snapshot with { MarketRegime = MarketRegime, SectorRegime = SectorRegime };
    }
}

/// <summary>Pure benchmark trend classification using complete-window SMAs.</summary>
public sealed class RegimeIndicatorCalculator
{
    private readonly SimpleMovingAverageIndicatorCalculator _sma = new();

    public RegimeContext Calculate(RegimeCalculationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.UnderlyingSymbol);
        ArgumentNullException.ThrowIfNull(request.MarketObservations);
        ArgumentNullException.ThrowIfNull(request.SectorObservations);
        ArgumentNullException.ThrowIfNull(request.Configuration);
        ArgumentNullException.ThrowIfNull(request.IndicatorCalculationVersion);
        if (request.CalculatedAt.Offset != TimeSpan.Zero) throw new ArgumentException("CalculatedAt must be UTC.", nameof(request));
        request.Configuration.Validate();

        var market = Classify(request.Configuration.Regime.MarketBenchmark, request.MarketObservations, request);
        var sector = string.IsNullOrWhiteSpace(request.SectorBenchmarkSymbol)
            ? MarketRegime.InsufficientData
            : Classify(request.SectorBenchmarkSymbol, request.SectorObservations, request);
        return new RegimeContext(request.UnderlyingSymbol, request.AsOfDate, market, sector,
            request.IndicatorCalculationVersion, request.Configuration.Version, request.CalculatedAt);
    }

    private MarketRegime Classify(string symbol, IReadOnlyList<IndicatorPriceObservation> source, RegimeCalculationRequest request)
    {
        var settings = request.Configuration.Regime;
        var observations = source.Where(x => string.Equals(x.Symbol, symbol, StringComparison.OrdinalIgnoreCase) && x.TradingDate <= request.AsOfDate)
            .OrderBy(x => x.TradingDate).ToArray();
        if (observations.Length <= settings.SlopeLookbackTradingDays || observations[^1].Close is not { } close)
            return MarketRegime.InsufficientData;

        // Reuse the Phase 3A SMA calculator with the regime's independent configured periods.
        var smaConfiguration = request.Configuration with
        {
            SimpleMovingAverage = new SimpleMovingAverageConfiguration
            {
                Sma20Period = settings.FastSmaPeriod,
                Sma50Period = settings.FastSmaPeriod,
                Sma200Period = settings.LongSmaPeriod
            }
        };
        IndicatorSnapshot SmaAt(DateOnly date) => _sma.Calculate(new IndicatorCalculationRequest(symbol, date, observations,
            smaConfiguration, request.IndicatorCalculationVersion, request.CalculatedAt));

        var current = SmaAt(observations[^1].TradingDate);
        var prior = SmaAt(observations[observations.Length - 1 - settings.SlopeLookbackTradingDays].TradingDate);
        if (current.Sma50.Value is not { } fast || current.Sma200.Value is not { } slow || prior.Sma50.Value is not { } priorFast)
            return MarketRegime.InsufficientData;

        if (close > slow && fast > slow && fast > priorFast) return MarketRegime.Bullish;
        if (close < slow && fast < slow && fast < priorFast) return MarketRegime.Bearish;
        return MarketRegime.Neutral;
    }
}
