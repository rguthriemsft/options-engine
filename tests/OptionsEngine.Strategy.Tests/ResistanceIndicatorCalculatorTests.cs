using OptionsEngine.Strategy.Indicators;

namespace OptionsEngine.Strategy.Tests;

public sealed class ResistanceIndicatorCalculatorTests
{
    private static readonly DateOnly Start = new(2026, 1, 1);
    private static readonly DateTimeOffset CalculatedAt = new(2026, 9, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly IndicatorCalculationVersion CalculationVersion = new("3.0.0-resistance.1");
    private static readonly ResistanceIndicatorCalculator Calculator = new();

    [Fact]
    public void CompleteLinkageRejectsTransitiveChainingAndUsesArithmeticMean()
    {
        var bars = Bars(40, (5, 100m), (15, 100.60m), (25, 101.20m));
        var result = Calculate(bars);

        Assert.Equal(2, result.Clusters.Count);
        Assert.Equal((100m, 100.60m, 2, 100.30m), (result.Clusters[0].MinimumPrice, result.Clusters[0].MaximumPrice,
            result.Clusters[0].TouchCount, result.Clusters[0].RepresentativePrice));
        Assert.Equal(101.20m, result.Clusters[1].RepresentativePrice);
        Assert.Equal(100.30m, result.ResistancePrice.Value);
        Assert.Equal(10.30m, result.DistanceToResistance.Value);
        Assert.Equal(10.30 / 90, result.DistanceToResistancePercent.Value!.Value, 12);
        Assert.Equal(2, result.ResistanceTouchCount.Value);
        Assert.Equal(Start.AddDays(15), result.ResistanceLastTouchDate.Value);
        Assert.Equal(24, result.ResistanceAgeTradingDays.Value);
        var reversed = Calculate(bars.Reverse().ToArray());
        Assert.Equal(result.Clusters.ToArray(), reversed.Clusters.ToArray());
        Assert.Equal(result.ResistancePrice, reversed.ResistancePrice);
    }

    [Theory]
    [InlineData("100.75", 1, true)]
    [InlineData("100.7501", 2, false)]
    public void InclusiveClusterBoundaryAndJustBeyondIt(string secondPrice, int expectedClusters, bool qualified)
    {
        var result = Calculate(Bars(40, (5, 100m), (15, decimal.Parse(secondPrice, System.Globalization.CultureInfo.InvariantCulture))));

        Assert.Equal(expectedClusters, result.Clusters.Count);
        Assert.Equal(qualified ? IndicatorValueStatus.Available : IndicatorValueStatus.InsufficientData, result.ResistancePrice.Status);
    }

    [Fact]
    public void MinimumTouchesAndStrictlyOverheadRepresentativeControlQualification()
    {
        var bars = Bars(40, (5, 100m));
        var unavailable = Calculate(bars);
        Assert.Null(unavailable.ResistancePrice.Value);
        Assert.Null(unavailable.DistanceToResistance.Value);
        Assert.Null(unavailable.DistanceToResistancePercent.Value);
        Assert.Null(unavailable.ResistanceTouchCount.Value);
        Assert.Null(unavailable.ResistanceLastTouchDate.Value);
        Assert.Null(unavailable.ResistanceAgeTradingDays.Value);
        Assert.Equal(100m, Calculate(bars, resistance: new ResistanceConfiguration { LookbackTradingDays = 40, MinimumResistanceTouches = 1 }).ResistancePrice.Value);
        Assert.Null(Calculate(Bars(40, (5, 100m), (15, 100.6m)), currentClose: 100.3m).ResistancePrice.Value);
        Assert.Equal(101.3m, Calculate(Bars(50, (5, 100m), (15, 100.6m), (25, 101m), (35, 101.6m)),
            resistance: new ResistanceConfiguration { LookbackTradingDays = 50 }, currentClose: 100.3m).ResistancePrice.Value);
        Assert.Null(Calculate(Bars(40, (5, 100m), (15, 100.6m)),
            resistance: new ResistanceConfiguration { LookbackTradingDays = 40, MinimumResistanceTouches = 3 }).ResistancePrice.Value);
    }

    [Fact]
    public void SwingConfirmationRequiresBothSidesAndExcludesFutureAtAsOfDate()
    {
        var bars = Bars(40, (35, 100m), (39, 101m));
        var settings = new ResistanceConfiguration { LookbackTradingDays = 40, MinimumResistanceTouches = 1 };
        var at39 = Calculate(bars, resistance: settings);
        Assert.Single(at39.Clusters);
        Assert.Equal(100m, at39.ResistancePrice.Value);
        Assert.Equal(4, at39.ResistanceAgeTradingDays.Value);
        var at37 = Calculate(bars, asOf: Start.AddDays(37), resistance: settings with { LookbackTradingDays = 38 });
        Assert.Empty(at37.Clusters);
        Assert.Null(at37.ResistancePrice.Value);
        var withFuture = Calculate(bars.Concat(Bars(3, dateOffset: 40)), asOf: Start.AddDays(37), resistance: settings with { LookbackTradingDays = 38 });
        Assert.Equal(at37.Clusters.ToArray(), withFuture.Clusters.ToArray());
        Assert.Equal(at37.ResistancePrice, withFuture.ResistancePrice);
    }

    [Fact]
    public void LookbackIncludesExactBoundaryAndExcludesEarlierCandidate()
    {
        var settings = new ResistanceConfiguration { MinimumResistanceTouches = 1 };
        var inside = Calculate(Bars(130, (10, 100m)), resistance: settings);
        var outside = Calculate(Bars(130, (9, 100m)), resistance: settings);
        Assert.Equal(100m, inside.ResistancePrice.Value);
        Assert.Empty(outside.Clusters);
        Assert.Null(outside.ResistancePrice.Value);
    }

    [Fact]
    public void ConfigurableSwingWindowAndMultiplierChangeDetectionAndGrouping()
    {
        var bars = Bars(40, (5, 100m), (15, 100.6m));
        Assert.Equal(2, Calculate(bars).ResistanceTouchCount.Value);
        Assert.Null(Calculate(bars, resistance: new ResistanceConfiguration { LookbackTradingDays = 40, ClusterDistanceAtrMultiplier = 0.5 }).ResistancePrice.Value);
        Assert.Equal(2, Calculate(bars, resistance: new ResistanceConfiguration { LookbackTradingDays = 40, SwingWindow = 1 }).ResistanceTouchCount.Value);
        var early = Bars(12, (1, 100m));
        Assert.Empty(Calculate(early).Clusters);
        Assert.Single(Calculate(early, resistance: new ResistanceConfiguration { LookbackTradingDays = 12, SwingWindow = 1, MinimumResistanceTouches = 1 }).Clusters);
    }

    [Fact]
    public void ClusteringUsesOneAsOfAtrRatherThanHistoricalCandidateAtr()
    {
        // ATR(1) at the as-of bar is exactly 1; the historical spike TRs are much larger.
        var result = Calculate(Bars(40, (5, 100m), (15, 101m)));
        Assert.Equal(2, result.Clusters.Count);
        Assert.Null(result.ResistancePrice.Value);
        Assert.Equal(100m, Calculate(Bars(40, (5, 100m), (15, 101m)),
            resistance: new ResistanceConfiguration { LookbackTradingDays = 40, MinimumResistanceTouches = 1 }).ResistancePrice.Value);
    }

    [Fact]
    public void TradingObservationAgeDoesNotCountCalendarGaps()
    {
        var bars = Bars(40, (5, 100m), (15, 100.6m))
            .Select((bar, index) => bar with { TradingDate = Start.AddDays(index * 2) }).ToArray();
        var result = Calculate(bars);
        Assert.Equal(24, result.ResistanceAgeTradingDays.Value);
        Assert.Equal(Start.AddDays(30), result.ResistanceLastTouchDate.Value);
    }

    [Fact]
    public void MissingAtrOrRequiredHighMakesResistanceUnavailableAndFutureDoesNotRepairHistory()
    {
        var bars = Bars(40, (5, 100m), (15, 100.6m));
        bars[^1] = bars[^1] with { High = null };
        var result = Calculate(bars);
        Assert.Empty(result.Clusters);
        Assert.Null(result.ResistancePrice.Value);
        var withFuture = Calculate(bars.Concat(Bars(3, dateOffset: 40)), asOf: Start.AddDays(39));
        Assert.Equal(result.Clusters.ToArray(), withFuture.Clusters.ToArray());
        Assert.Equal(result.ResistancePrice, withFuture.ResistancePrice);
    }

    [Fact]
    public void FuturePricesAndAtrCannotChangeAvailableHistoricalResistance()
    {
        var history = Bars(40, (5, 100m), (15, 100.6m));
        var original = Calculate(history);
        var future = Bars(5, dateOffset: 40);
        future[^1] = future[^1] with { High = 500m };
        var appended = Calculate(history.Concat(future), asOf: Start.AddDays(39));

        Assert.Equal(100.3m, original.ResistancePrice.Value);
        Assert.Equal(original.Clusters.ToArray(), appended.Clusters.ToArray());
        Assert.Equal(original.ResistancePrice, appended.ResistancePrice);
        Assert.Equal(original.ResistanceAgeTradingDays, appended.ResistanceAgeTradingDays);
    }

    [Fact]
    public void ConfigurationRejectsInvalidMinimumTouches()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Calculate(Bars(40), resistance: new ResistanceConfiguration { MinimumResistanceTouches = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Calculate(Bars(40), resistance: new ResistanceConfiguration { ClusterDistanceAtrMultiplier = double.NaN }));
    }

    [Fact]
    public void ResistanceRequiresTheCompleteConfiguredLookback()
    {
        var bars = Bars(119, (20, 100m), (40, 100.6m));
        var settings = new ResistanceConfiguration();
        Assert.Empty(Calculate(bars, resistance: settings).Clusters);
        Assert.Null(Calculate(bars, resistance: settings).ResistancePrice.Value);
        Assert.Equal(100.3m, Calculate(Bars(120, (20, 100m), (40, 100.6m)), resistance: settings).ResistancePrice.Value);
    }

    [Fact]
    public void ContextAppliesOnlyToMatchingVersionedSnapshot()
    {
        var bars = Bars(40, (5, 100m), (15, 100.6m));
        var configuration = new IndicatorConfiguration { Version = new ConfigurationVersion(1),
            AverageTrueRange = new AverageTrueRangeConfiguration { Period = 1 },
            Resistance = new ResistanceConfiguration { LookbackTradingDays = 40 } };
        var request = new IndicatorCalculationRequest("MSFT", Start.AddDays(39), bars, configuration, CalculationVersion, CalculatedAt);
        var context = Calculator.Calculate(request);
        var snapshot = new CoreTechnicalIndicatorCalculator().Calculate(request);
        Assert.Equal(100.3m, context.ApplyTo(snapshot).ResistancePrice.Value);
        Assert.Throws<ArgumentException>(() => context.ApplyTo(snapshot with { AsOfDate = Start.AddDays(38) }));
    }

    private static ResistanceContext Calculate(IEnumerable<IndicatorPriceObservation> source, DateOnly? asOf = null,
        ResistanceConfiguration? resistance = null, decimal currentClose = 90m)
    {
        var bars = source.ToArray();
        var date = asOf ?? bars.Max(x => x.TradingDate);
        if (currentClose != 90m)
        {
            var index = Array.FindIndex(bars, x => x.TradingDate == date);
            bars[index] = bars[index] with { Close = currentClose };
        }
        return Calculator.Calculate(new IndicatorCalculationRequest("MSFT", date, bars,
            new IndicatorConfiguration { Version = new ConfigurationVersion(1), AverageTrueRange = new AverageTrueRangeConfiguration { Period = 1 },
                Resistance = resistance ?? new ResistanceConfiguration { LookbackTradingDays = Math.Min(40, bars.Length) } }, CalculationVersion, CalculatedAt));
    }

    private static IndicatorPriceObservation[] Bars(int count, params (int Index, decimal High)[] highs) => Bars(count, 0, highs);

    private static IndicatorPriceObservation[] Bars(int count, int dateOffset, params (int Index, decimal High)[] highs) =>
        Enumerable.Range(0, count).Select(i => new IndicatorPriceObservation("MSFT", Start.AddDays(i + dateOffset), 90m,
            highs.FirstOrDefault(x => x.Index == i).High is { } high && high > 0 ? high : i == count - 1 ? 91m : 90m,
            90m, 90m, 1000)).ToArray();
}
