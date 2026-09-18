using OptionsEngine.Strategy.Indicators;

namespace OptionsEngine.Strategy.Tests;

public sealed class SimpleMovingAverageIndicatorCalculatorTests
{
    private static readonly SimpleMovingAverageIndicatorCalculator Calculator = new();
    private static readonly IndicatorConfiguration Configuration = new()
    {
        Version = new ConfigurationVersion(1)
    };
    private static readonly IndicatorCalculationVersion CalculationVersion = new("3.0.0-sma.1");
    private static readonly DateTimeOffset CalculatedAt = new(2026, 9, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Start = new(2026, 1, 1);

    [Theory]
    [InlineData(19, IndicatorValueStatus.InsufficientData, null)]
    [InlineData(20, IndicatorValueStatus.Available, "10.5")]
    [InlineData(49, IndicatorValueStatus.InsufficientData, null)]
    [InlineData(50, IndicatorValueStatus.Available, "25.5")]
    [InlineData(199, IndicatorValueStatus.InsufficientData, null)]
    [InlineData(200, IndicatorValueStatus.Available, "100.5")]
    public void CalculatesOnlyAtExactCompleteWindowBoundaries(int count, IndicatorValueStatus expectedStatus, string? expectedValue)
    {
        var snapshot = Calculate(Bars(count));
        var value = count <= 20 ? snapshot.Sma20 : count <= 50 ? snapshot.Sma50 : snapshot.Sma200;

        Assert.Equal(expectedStatus, value.Status);
        Assert.Equal(expectedValue is null ? null : decimal.Parse(expectedValue, System.Globalization.CultureInfo.InvariantCulture), value.Value);
    }

    [Fact]
    public void ReturnsInsufficientDataForEverySmaWithTooLittleHistory()
    {
        var snapshot = Calculate(Bars(19));

        Assert.All(new[] { snapshot.Sma20, snapshot.Sma50, snapshot.Sma200 }, value =>
        {
            Assert.Equal(IndicatorValueStatus.InsufficientData, value.Status);
            Assert.Null(value.Value);
        });
    }

    [Fact]
    public void IncludesTheCurrentObservationInTheSmaWindow()
    {
        var bars = Bars(20).ToList();
        bars[^1] = bars[^1] with { Close = 200m };

        var snapshot = Calculate(bars);

        Assert.Equal(19.5m, snapshot.Sma20.Value);
        Assert.Equal(IndicatorValueStatus.Available, snapshot.Sma20.Status);
    }

    [Fact]
    public void OrdersObservationsChronologicallyBeforeChoosingTheWindow()
    {
        var chronological = Bars(21).ToArray();
        var reverseChronological = chronological.Reverse().ToArray();

        var ordered = Calculate(chronological);
        var unordered = Calculate(reverseChronological);

        Assert.Equal(ordered.Sma20, unordered.Sma20);
        Assert.Equal(11.5m, unordered.Sma20.Value);
    }

    [Fact]
    public void ExcludesFutureObservationsFromAnAsOfCalculation()
    {
        var asOfDate = Start.AddDays(19);
        var observations = Bars(20).Append(Bar(20, 1_000m)).ToArray();

        var snapshot = Calculate(observations, asOfDate);

        Assert.Equal(10.5m, snapshot.Sma20.Value);
    }

    [Fact]
    public void AppendingFutureObservationsDoesNotChangeHistoricalResult()
    {
        var asOfDate = Start.AddDays(49);
        var historical = Bars(50).ToArray();
        var withFuture = historical.Concat(Bars(10, 10_000m, 50)).ToArray();

        var original = Calculate(historical, asOfDate);
        var appended = Calculate(withFuture, asOfDate);

        Assert.Equal(original, appended);
    }

    [Fact]
    public void DistinguishesAValidZeroSmaFromAnUnavailableSma()
    {
        var zeroSnapshot = Calculate(Bars(20).Select(x => x with { Close = 0m }));
        var unavailableSnapshot = Calculate(Bars(19).Select(x => x with { Close = 0m }));

        Assert.Equal(IndicatorValueStatus.Available, zeroSnapshot.Sma20.Status);
        Assert.Equal(0m, zeroSnapshot.Sma20.Value);
        Assert.Equal(IndicatorValueStatus.InsufficientData, unavailableSnapshot.Sma20.Status);
        Assert.Null(unavailableSnapshot.Sma20.Value);
    }

    [Fact]
    public void MissingCloseInsideTheLastTwentyTradingObservationsMakesSma20Unavailable()
    {
        var observations = Bars(21).ToList();
        observations[^1] = observations[^1] with { Close = null };

        var snapshot = Calculate(observations);

        Assert.Equal(IndicatorValueStatus.InsufficientData, snapshot.Sma20.Status);
        Assert.Null(snapshot.Sma20.Value);
    }

    [Fact]
    public void MissingCloseOutsideTheLastTwentyTradingObservationsDoesNotMakeSma20Unavailable()
    {
        var observations = Bars(21).ToList();
        observations[0] = observations[0] with { Close = null };

        var snapshot = Calculate(observations);

        Assert.Equal(IndicatorValueStatus.Available, snapshot.Sma20.Status);
        Assert.Equal(11.5m, snapshot.Sma20.Value);
    }

    [Fact]
    public void RetainsTheIndependentCalculationAndConfigurationVersionsInTheSnapshot()
    {
        var snapshot = Calculate(Bars(20));

        Assert.Equal(CalculationVersion, snapshot.IndicatorCalculationVersion);
        Assert.Equal(Configuration.Version, snapshot.ConfigurationVersion);
        Assert.Equal(CalculatedAt, snapshot.CalculatedAt);
    }

    private static IndicatorSnapshot Calculate(IEnumerable<IndicatorPriceObservation> observations, DateOnly? asOfDate = null) =>
        Calculator.Calculate(new IndicatorCalculationRequest("MSFT", asOfDate ?? observations.Max(x => x.TradingDate), observations.ToArray(), Configuration, CalculationVersion, CalculatedAt));

    private static IEnumerable<IndicatorPriceObservation> Bars(int count, decimal startClose = 1m, int dateOffset = 0) =>
        Enumerable.Range(0, count).Select(index => Bar(index + dateOffset, startClose + index));

    private static IndicatorPriceObservation Bar(int index, decimal close) =>
        new("MSFT", Start.AddDays(index), close, close, close, close, 1_000);
}
