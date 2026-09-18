using OptionsEngine.Strategy.Indicators;

namespace OptionsEngine.Strategy.Tests;

public sealed class CoreTechnicalIndicatorCalculatorTests
{
    private static readonly CoreTechnicalIndicatorCalculator Calculator = new();
    private static readonly IndicatorConfiguration Configuration = new() { Version = new ConfigurationVersion(1) };
    private static readonly IndicatorCalculationVersion CalculationVersion = new("3.0.0-core.1");
    private static readonly DateTimeOffset CalculatedAt = new(2026, 9, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Start = new(2026, 1, 1);

    [Fact]
    public void CalculatesInitialRsiAndWilderSmoothing()
    {
        var closes = Enumerable.Range(1, 15).Select(x => (decimal)x).Append(14m);

        var snapshot = Calculate(Bars(closes));

        AssertClose(92.85714285714286, snapshot.Rsi14.Value);
    }

    [Theory]
    [InlineData(1, 100)]
    [InlineData(-1, 0)]
    [InlineData(0, 50)]
    public void HandlesAllGainsAllLossesAndFlatRsiDeterministically(int change, double expected)
    {
        var closes = Enumerable.Range(0, 15).Select(index => 100m + (index * change));

        var snapshot = Calculate(Bars(closes));

        AssertClose(expected, snapshot.Rsi14.Value);
    }

    [Fact]
    public void RsiRequiresFifteenCompleteClosingObservations()
    {
        var insufficient = Calculate(Bars(Enumerable.Range(1, 14).Select(x => (decimal)x)));
        var complete = Calculate(Bars(Enumerable.Range(1, 15).Select(x => (decimal)x)));

        AssertUnavailable(insufficient.Rsi14);
        Assert.Equal(IndicatorValueStatus.Available, complete.Rsi14.Status);
    }

    [Fact]
    public void CalculatesPopulationBollingerBandsAndUnclampedPercentB()
    {
        var snapshot = Calculate(Bars(Enumerable.Repeat(10m, 19).Append(20m)));

        AssertClose(10.5, snapshot.BollingerMiddle.Value);
        AssertClose(14.858898943540673, snapshot.BollingerUpper.Value);
        AssertClose(6.141101056459326, snapshot.BollingerLower.Value);
        AssertClose(1.5897247358851685, snapshot.BollingerPercentB.Value);
        AssertClose(0.8302664654363187, snapshot.BollingerBandwidth.Value);
        Assert.True(snapshot.BollingerPercentB.Value > 1);
    }

    [Fact]
    public void BollingerPercentBCanBeBelowZero()
    {
        var snapshot = Calculate(Bars(Enumerable.Repeat(10m, 19).Append(0m)));

        AssertClose(-0.5897247358851685, snapshot.BollingerPercentB.Value);
        Assert.True(snapshot.BollingerPercentB.Value < 0);
    }

    [Fact]
    public void LeavesPercentBUnavailableForZeroWidthBandsButRetainsZeroBandwidth()
    {
        var snapshot = Calculate(Bars(Enumerable.Repeat(10m, 20)));

        AssertUnavailable(snapshot.BollingerPercentB);
        AssertClose(0, snapshot.BollingerBandwidth.Value);
    }

    [Fact]
    public void LeavesBandwidthUnavailableWhenTheMiddleBandIsZero()
    {
        var snapshot = Calculate(Bars(Enumerable.Repeat(0m, 20)));

        AssertUnavailable(snapshot.BollingerBandwidth);
    }

    [Fact]
    public void MacdUsesCompletePeriodSmaSeedsAndAllowsSignalWarmup()
    {
        var beforeSlowSeed = Calculate(Bars(Enumerable.Range(1, 25).Select(x => (decimal)x)));
        var beforeSignalSeed = Calculate(Bars(Enumerable.Range(1, 26).Select(x => (decimal)x)));
        var complete = Calculate(Bars(Enumerable.Range(1, 34).Select(x => (decimal)x)));

        AssertUnavailable(beforeSlowSeed.Macd);
        AssertClose(7, beforeSignalSeed.Macd.Value);
        AssertUnavailable(beforeSignalSeed.MacdSignal);
        AssertUnavailable(beforeSignalSeed.MacdHistogram);
        AssertClose(7, complete.Macd.Value);
        AssertClose(7, complete.MacdSignal.Value);
        AssertClose(0, complete.MacdHistogram.Value);
    }

    [Fact]
    public void CalculatesInitialAtrWilderSmoothingAndAtrPercent()
    {
        var observations = Enumerable.Range(0, 15).Select(index => Observation(index, 8m, 10m, 9m)).Append(Observation(15, 13m, 14m, 13m));

        var snapshot = Calculate(observations);

        AssertClose(31d / 14d, snapshot.Atr14.Value);
        AssertClose((31d / 14d) / 13d, snapshot.AtrPercent.Value);
    }

    [Fact]
    public void AtrRequiresFourteenTrueRangesAndDoesNotTreatMissingOhlcAsZero()
    {
        var insufficient = Calculate(Enumerable.Range(0, 14).Select(index => Observation(index, 8m, 10m, 9m)));
        var missing = Bars(Enumerable.Range(1, 15).Select(x => (decimal)x)).ToList();
        missing[^1] = missing[^1] with { High = null };

        AssertUnavailable(insufficient.Atr14);
        AssertUnavailable(Calculate(missing).Atr14);
    }

    [Theory]
    [InlineData(8, 12, 4)]
    [InlineData(14, 15, 6)]
    [InlineData(3, 4, 6)]
    public void AtrTrueRangeSelectsHighLowAndBothPreviousCloseGapCases(decimal low, decimal high, double expected)
    {
        var configuration = Configuration with { AverageTrueRange = new AverageTrueRangeConfiguration { Period = 1 } };
        var observations = new[] { Observation(0, 8m, 10m, 9m), Observation(1, low, high, (low + high) / 2) };

        var snapshot = Calculate(observations, configuration: configuration);

        AssertClose(expected, snapshot.Atr14.Value);
    }

    [Fact]
    public void CalculatesAnnualizedSampleStandardDeviationForRealizedVolatility()
    {
        var returns = Enumerable.Range(0, 20).Select(index => index % 2 == 0 ? 0.01 : -0.01).ToArray();
        var closes = ClosesFromReturns(returns);
        var configuration = Configuration with { RealizedVolatility = new RealizedVolatilityConfiguration { ShortPeriod = 20, StandardPeriod = 20, AnnualizationTradingDays = 252 } };

        var snapshot = Calculate(Bars(closes), configuration: configuration);
        var expected = Math.Sqrt(returns.Sum(value => value * value) / 19d) * Math.Sqrt(252);

        AssertClose(expected, snapshot.RealizedVolatility20.Value, 1e-9);
        AssertClose(expected, snapshot.RealizedVolatility30.Value, 1e-9);
    }

    [Fact]
    public void DistinguishesZeroRealizedVolatilityFromInsufficientHistory()
    {
        var zero = Calculate(Bars(Enumerable.Repeat(100m, 31)));
        var insufficient = Calculate(Bars(Enumerable.Repeat(100m, 20)));

        AssertClose(0, zero.RealizedVolatility20.Value);
        AssertClose(0, zero.RealizedVolatility30.Value);
        AssertUnavailable(insufficient.RealizedVolatility20);
        AssertUnavailable(insufficient.RealizedVolatility30);
    }

    [Fact]
    public void RealizedVolatilityUsesExactTwentyAndThirtyReturnWarmupBoundaries()
    {
        var twentyReturns = Calculate(Bars(Enumerable.Repeat(100m, 21)));
        var oneBelowThirtyReturns = Calculate(Bars(Enumerable.Repeat(100m, 30)));
        var thirtyReturns = Calculate(Bars(Enumerable.Repeat(100m, 31)));

        Assert.Equal(IndicatorValueStatus.Available, twentyReturns.RealizedVolatility20.Status);
        AssertUnavailable(twentyReturns.RealizedVolatility30);
        Assert.Equal(IndicatorValueStatus.Available, oneBelowThirtyReturns.RealizedVolatility20.Status);
        AssertUnavailable(oneBelowThirtyReturns.RealizedVolatility30);
        Assert.Equal(IndicatorValueStatus.Available, thirtyReturns.RealizedVolatility30.Status);
    }

    [Fact]
    public void CoreIndicatorsUseOnlyObservationsAtOrBeforeAsOfDate()
    {
        var historical = Bars(Enumerable.Range(1, 40).Select(x => (decimal)x)).ToArray();
        var withFuture = historical.Concat(Bars(Enumerable.Repeat(1_000m, 10), 40)).ToArray();
        var asOfDate = historical[^1].TradingDate;

        Assert.Equal(Calculate(historical, asOfDate), Calculate(withFuture, asOfDate));
    }

    [Fact]
    public void MissingCloseInsideRequiredTrailingWindowsMakesDependentIndicatorsUnavailable()
    {
        var observations = Bars(Enumerable.Range(1, 40).Select(x => (decimal)x)).ToList();
        observations[^1] = observations[^1] with { Close = null };

        var snapshot = Calculate(observations);

        AssertUnavailable(snapshot.Rsi14);
        AssertUnavailable(snapshot.BollingerMiddle);
        AssertUnavailable(snapshot.Macd);
        AssertUnavailable(snapshot.RealizedVolatility20);
    }

    [Theory]
    [InlineData(60, false)] // First observation in the last 20.
    [InlineData(59, true)]  // Immediately outside the last 20.
    [InlineData(5, true)]   // Substantially earlier.
    public void BollingerUsesExactlyTheLastTwentyTradingObservations(int missingIndex, bool available)
    {
        var observations = Bars(Enumerable.Range(1, 80).Select(x => (decimal)x)).ToArray();
        observations[missingIndex] = observations[missingIndex] with { Close = null };

        var snapshot = Calculate(observations);

        if (available)
        {
            AssertClose(70.5, snapshot.BollingerMiddle.Value);
            Assert.Equal(IndicatorValueStatus.Available, snapshot.BollingerPercentB.Status);
        }
        else
        {
            AssertUnavailable(snapshot.BollingerMiddle);
            AssertUnavailable(snapshot.BollingerUpper);
            AssertUnavailable(snapshot.BollingerLower);
            AssertUnavailable(snapshot.BollingerPercentB);
            AssertUnavailable(snapshot.BollingerBandwidth);
        }
    }

    [Theory]
    [InlineData(59, false, false)] // First close required for RV20.
    [InlineData(58, true, false)]  // Immediately outside RV20, inside RV30.
    [InlineData(49, true, false)]  // First close required for RV30.
    [InlineData(48, true, true)]   // Immediately outside RV30.
    [InlineData(5, true, true)]    // Substantially earlier.
    public void RealizedVolatilityUsesOnlyItsLastNPlusOneCloses(int missingIndex, bool rv20Available, bool rv30Available)
    {
        var observations = Bars(Enumerable.Repeat(100m, 80)).ToArray();
        observations[missingIndex] = observations[missingIndex] with { Close = null };

        var snapshot = Calculate(observations);

        if (rv20Available) AssertClose(0, snapshot.RealizedVolatility20.Value);
        else AssertUnavailable(snapshot.RealizedVolatility20);
        if (rv30Available) AssertClose(0, snapshot.RealizedVolatility30.Value);
        else AssertUnavailable(snapshot.RealizedVolatility30);
    }

    [Theory]
    [InlineData(65)] // After the initial recursive seed.
    [InlineData(25)] // At the slow EMA seed boundary.
    [InlineData(5)]  // Substantially earlier in the initialization history.
    public void MissingHistoricalCloseDoesNotReseedRsiOrMacd(int missingIndex)
    {
        var observations = Bars(Enumerable.Range(1, 80).Select(x => (decimal)x)).ToArray();
        observations[missingIndex] = observations[missingIndex] with { Close = null };

        var snapshot = Calculate(observations);

        AssertUnavailable(snapshot.Rsi14);
        AssertUnavailable(snapshot.Macd);
        AssertUnavailable(snapshot.MacdSignal);
        AssertUnavailable(snapshot.MacdHistogram);
    }

    [Theory]
    [InlineData(65)] // After the initial ATR seed.
    [InlineData(14)] // At the initial ATR seed boundary.
    [InlineData(5)]  // Substantially earlier in the True Range history.
    public void MissingHistoricalHighDoesNotReseedWilderAtr(int missingIndex)
    {
        var observations = Bars(Enumerable.Range(1, 80).Select(x => (decimal)x)).ToArray();
        observations[missingIndex] = observations[missingIndex] with { High = null };

        var snapshot = Calculate(observations);

        AssertUnavailable(snapshot.Atr14);
        AssertUnavailable(snapshot.AtrPercent);
    }

    [Fact]
    public void FirstObservationHighAndLowAreOutsideAtrTrueRangeHistory()
    {
        var observations = Bars(Enumerable.Range(1, 80).Select(x => (decimal)x)).ToArray();
        observations[0] = observations[0] with { High = null, Low = null };

        var snapshot = Calculate(observations);

        Assert.Equal(IndicatorValueStatus.Available, snapshot.Atr14.Status);
        Assert.Equal(IndicatorValueStatus.Available, snapshot.AtrPercent.Status);
    }

    [Fact]
    public void RejectsInvalidCoreIndicatorConfiguration()
    {
        var configuration = Configuration with { Macd = new MovingAverageConvergenceDivergenceConfiguration { FastPeriod = 26, SlowPeriod = 12 } };

        Assert.Throws<ArgumentOutOfRangeException>(() => Calculate(Bars(Enumerable.Range(1, 40).Select(x => (decimal)x)), configuration: configuration));
    }

    private static IndicatorSnapshot Calculate(IEnumerable<IndicatorPriceObservation> observations, DateOnly? asOfDate = null, IndicatorConfiguration? configuration = null)
    {
        var input = observations.ToArray();
        return Calculator.Calculate(new IndicatorCalculationRequest("MSFT", asOfDate ?? input.Max(x => x.TradingDate), input, configuration ?? Configuration, CalculationVersion, CalculatedAt));
    }

    private static IEnumerable<IndicatorPriceObservation> Bars(IEnumerable<decimal> closes, int dateOffset = 0) =>
        closes.Select((close, index) => Observation(index + dateOffset, close, close, close));

    private static IndicatorPriceObservation Observation(int index, decimal low, decimal high, decimal close) =>
        new("MSFT", Start.AddDays(index), low, high, low, close, 1_000);

    private static decimal[] ClosesFromReturns(IReadOnlyList<double> returns)
    {
        var closes = new decimal[returns.Count + 1];
        closes[0] = 100m;
        for (var index = 0; index < returns.Count; index++) closes[index + 1] = closes[index] * (decimal)Math.Exp(returns[index]);
        return closes;
    }

    private static void AssertUnavailable(IndicatorValue<double> value)
    {
        Assert.Equal(IndicatorValueStatus.InsufficientData, value.Status);
        Assert.Null(value.Value);
    }

    private static void AssertClose(double expected, double? actual, double tolerance = 1e-10)
    {
        Assert.NotNull(actual);
        Assert.InRange(actual!.Value, expected - tolerance, expected + tolerance);
    }
}
