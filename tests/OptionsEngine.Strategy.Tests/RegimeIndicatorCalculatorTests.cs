using OptionsEngine.Strategy.Indicators;

namespace OptionsEngine.Strategy.Tests;

public sealed class RegimeIndicatorCalculatorTests
{
    private static readonly DateOnly Start = new(2026, 1, 1);
    private static readonly DateTimeOffset CalculatedAt = new(2026, 9, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly IndicatorCalculationVersion CalculationVersion = new("3.0.0-regime.1");
    private static readonly RegimeIndicatorCalculator Calculator = new();

    [Theory]
    [InlineData(1, MarketRegime.Bullish)]
    [InlineData(-1, MarketRegime.Bearish)]
    [InlineData(0, MarketRegime.Neutral)]
    public void MarketAndSectorUseTheSameClassificationRules(int slope, MarketRegime expected)
    {
        var market = Bars("SPY", 220, slope);
        var sector = Bars("XLK", 220, slope);
        var result = Calculate(market, "XLK", sector);
        Assert.Equal(expected, result.MarketRegime);
        Assert.Equal(expected, result.SectorRegime);
    }

    [Fact]
    public void MissingMappingOrBenchmarkDataIsInsufficientNotNeutral()
    {
        var market = Bars("SPY", 220, 1);
        Assert.Equal(MarketRegime.InsufficientData, Calculate(market).SectorRegime);
        Assert.Equal(MarketRegime.InsufficientData, Calculate(market, "XLK", []).SectorRegime);
        Assert.Equal(MarketRegime.Bullish, Calculate(market).MarketRegime);
    }

    [Fact]
    public void MarketAndSectorMayDifferAndConfiguredMarketBenchmarkIsUsed()
    {
        var configuration = Configuration() with { Regime = new RegimeConfiguration { MarketBenchmark = "QQQ" } };
        var market = Bars("QQQ", 220, 1).Concat(Bars("SPY", 220, -1)).ToArray();
        var result = Calculate(market, "XLK", Bars("XLK", 220, -1), configuration);
        Assert.Equal(MarketRegime.Bullish, result.MarketRegime);
        Assert.Equal(MarketRegime.Bearish, result.SectorRegime);
    }

    [Fact]
    public void DirectionUsesExactPriorTradingObservationAndRequiresCompleteSmaHistory()
    {
        var configuration = Configuration() with { Regime = new RegimeConfiguration
        {
            FastSmaPeriod = 2, LongSmaPeriod = 3, SlopeLookbackTradingDays = 2
        } };
        var bars = Bars("SPY", 4, 1);
        Assert.Equal(MarketRegime.InsufficientData, Calculate(bars.Take(3).ToArray(), configuration: configuration).MarketRegime);
        Assert.Equal(MarketRegime.Bullish, Calculate(bars, configuration: configuration).MarketRegime);
        Assert.Equal(MarketRegime.Bullish, Calculate(bars.Reverse().ToArray(), configuration: configuration).MarketRegime);
    }

    [Fact]
    public void MissingCurrentOrPriorRequiredCloseIsInsufficient()
    {
        var market = Bars("SPY", 220, 1);
        market[^1] = market[^1] with { Close = null };
        Assert.Equal(MarketRegime.InsufficientData, Calculate(market).MarketRegime);
        market = Bars("SPY", 220, 1);
        market[199] = market[199] with { Close = null }; // Required by the current SMA200.
        Assert.Equal(MarketRegime.InsufficientData, Calculate(market).MarketRegime);
        market = Bars("SPY", 220, 1);
        market[150] = market[150] with { Close = null }; // Required by the prior SMA50 at index 199.
        Assert.Equal(MarketRegime.InsufficientData, Calculate(market).MarketRegime);
    }

    [Fact]
    public void FutureObservationsCannotChangeHistoricalRegimes()
    {
        var market = Bars("SPY", 220, 1);
        var sector = Bars("XLK", 220, -1);
        var original = Calculate(market, "XLK", sector);
        var withFuture = Calculate(market.Concat(Bars("SPY", 10, -1, 220)).ToArray(), "XLK",
            sector.Concat(Bars("XLK", 10, 1, 220)).ToArray(), asOf: Start.AddDays(219));
        Assert.Equal(original, withFuture);
    }

    [Fact]
    public void AsOfDateAfterLatestTradingObservationRetainsRequestedIdentityAndRegimes()
    {
        var market = Bars("SPY", 220, 1);
        var sector = Bars("XLK", 220, -1);
        var latestTradingDate = market[^1].TradingDate;
        var requestedAsOfDate = latestTradingDate.AddDays(3);

        var atLastObservation = Calculate(market, "XLK", sector, asOf: latestTradingDate);
        var afterLastObservation = Calculate(market, "XLK", sector, asOf: requestedAsOfDate);

        Assert.Equal(requestedAsOfDate, afterLastObservation.AsOfDate);
        Assert.Equal(MarketRegime.Bullish, afterLastObservation.MarketRegime);
        Assert.Equal(MarketRegime.Bearish, afterLastObservation.SectorRegime);
        Assert.Equal(atLastObservation.MarketRegime, afterLastObservation.MarketRegime);
        Assert.Equal(atLastObservation.SectorRegime, afterLastObservation.SectorRegime);
    }

    [Fact]
    public void CalendarGapAsOfDateUsesPrecedingMarketAndSectorBarsAndExcludesFutureBars()
    {
        var market = Bars("SPY", 220, 1).Select((bar, index) => bar with { TradingDate = Start.AddDays(index * 2) }).ToArray();
        var sector = Bars("XLK", 220, -1).Select((bar, index) => bar with { TradingDate = Start.AddDays(index * 2) }).ToArray();
        var latestTradingDate = market[^1].TradingDate;
        var requestedAsOfDate = latestTradingDate.AddDays(1);
        var atLastObservation = Calculate(market, "XLK", sector, asOf: latestTradingDate);
        var inGap = Calculate(market, "XLK", sector, asOf: requestedAsOfDate);
        var futureMarket = new IndicatorPriceObservation("SPY", requestedAsOfDate.AddDays(1), 1m, 1m, 1m, 1m, 1000);
        var futureSector = new IndicatorPriceObservation("XLK", requestedAsOfDate.AddDays(1), 1000m, 1000m, 1000m, 1000m, 1000);
        var withFuture = Calculate(market.Append(futureMarket).ToArray(), "XLK", sector.Append(futureSector).ToArray(),
            asOf: requestedAsOfDate);

        Assert.Equal(requestedAsOfDate, inGap.AsOfDate);
        Assert.Equal(MarketRegime.Bullish, inGap.MarketRegime);
        Assert.Equal(MarketRegime.Bearish, inGap.SectorRegime);
        Assert.Equal(atLastObservation.MarketRegime, inGap.MarketRegime);
        Assert.Equal(atLastObservation.SectorRegime, inGap.SectorRegime);
        Assert.Equal(inGap, withFuture);
    }

    [Fact]
    public void VersionsAndSnapshotApplicationArePreserved()
    {
        var market = Bars("SPY", 220, 1);
        var result = Calculate(market);
        var snapshot = new SimpleMovingAverageIndicatorCalculator().Calculate(new IndicatorCalculationRequest("MSFT", Start.AddDays(219),
            Bars("MSFT", 220, 0), Configuration(), CalculationVersion, CalculatedAt));
        var applied = result.ApplyTo(snapshot);
        Assert.Equal(MarketRegime.Bullish, applied.MarketRegime);
        Assert.Equal(result.ConfigurationVersion, applied.ConfigurationVersion);
        Assert.Equal(result.IndicatorCalculationVersion, applied.IndicatorCalculationVersion);
    }

    [Fact]
    public void RegimeConfigurationRejectsInvalidDirectionLookbackAndBenchmark()
    {
        var market = Bars("SPY", 220, 1);
        Assert.Throws<ArgumentOutOfRangeException>(() => Calculate(market, configuration: Configuration() with
        {
            Regime = new RegimeConfiguration { SlopeLookbackTradingDays = 0 }
        }));
        Assert.Throws<ArgumentException>(() => Calculate(market, configuration: Configuration() with
        {
            Regime = new RegimeConfiguration { MarketBenchmark = " " }
        }));
    }

    private static RegimeContext Calculate(IReadOnlyList<IndicatorPriceObservation> market, string? sector = null,
        IReadOnlyList<IndicatorPriceObservation>? sectorBars = null, IndicatorConfiguration? configuration = null, DateOnly? asOf = null) =>
        Calculator.Calculate(new RegimeCalculationRequest("MSFT", asOf ?? Start.AddDays(219), market, sector, sectorBars ?? [],
            configuration ?? Configuration(), CalculationVersion, CalculatedAt));

    private static IndicatorConfiguration Configuration() => new() { Version = new ConfigurationVersion(1) };

    private static IndicatorPriceObservation[] Bars(string symbol, int count, int slope, int offset = 0) =>
        Enumerable.Range(0, count).Select(i =>
        {
            var close = 200m + (slope * i);
            return new IndicatorPriceObservation(symbol, Start.AddDays(i + offset), close, close, close, close, 1000);
        }).ToArray();
}
