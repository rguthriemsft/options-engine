using OptionsEngine.Strategy.Indicators;

namespace OptionsEngine.Strategy.Tests;

public sealed class ImpliedVolatilityContextCalculatorTests
{
    private static readonly ImpliedVolatilityContextCalculator Calculator = new();
    private static readonly DateOnly AsOfDate = new(2026, 1, 5);
    private static readonly DateTimeOffset CalculatedAt = new(2026, 1, 5, 23, 0, 0, TimeSpan.Zero);
    private static readonly IndicatorCalculationVersion CalculationVersion = new("3.0.0-iv.1");
    private static readonly IndicatorConfiguration Configuration = new() { Version = new ConfigurationVersion(1) };

    [Fact]
    public void ExactTargetDteUsesArithmeticCallPutMeanAndDecimalRatio()
    {
        var result = Calculate([Chain(30, 0.20, 0.40)]);

        AssertValue(0.30, result.Iv30);
        Assert.Null(result.Iv30UnavailableReason);
    }

    [Fact]
    public void InterpolatesLinearlyBetweenNearestQualifiedExpirations()
    {
        var result = Calculate([Chain(20, 0.20, 0.20), Chain(40, 0.40, 0.40), Chain(10, 0.05, 0.05), Chain(50, 0.90, 0.90)]);

        AssertValue(0.30, result.Iv30);
    }

    [Theory]
    [InlineData(40)]
    [InlineData(20)]
    public void DoesNotExtrapolateWithOnlyOneSideOfTargetDte(int dte)
    {
        var result = Calculate([Chain(dte, 0.30, 0.30)]);

        AssertUnavailable(result.Iv30);
        Assert.Equal(Iv30UnavailableReason.NoExactOrBracketingExpirations, result.Iv30UnavailableReason);
    }

    [Fact]
    public void SelectsNearestStrikeEvenWhenAnotherStrikeHasDifferentIv()
    {
        var chain = Chain(30, 0.20, 0.30, contracts:
        [Call(99m, 0.20), Put(99m, 0.30), Call(104m, 0.90), Put(104m, 0.90)]);

        AssertValue(0.25, Calculate([chain]).Iv30);
    }

    [Fact]
    public void EqualStrikeDistanceSelectsLowerStrike()
    {
        var chain = Chain(30, 0.20, 0.30, contracts:
        [Call(99m, 0.20), Put(99m, 0.30), Call(101m, 0.80), Put(101m, 0.90)]);

        AssertValue(0.25, Calculate([chain]).Iv30);
    }

    [Theory]
    [InlineData("95", true)]
    [InlineData("94.99", false)]
    public void AtmDistanceLimitIsInclusive(string strikeText, bool eligible)
    {
        var strike = decimal.Parse(strikeText, System.Globalization.CultureInfo.InvariantCulture);
        var result = Calculate([Chain(30, 0.20, 0.40, strike: strike)]);

        if (eligible) AssertValue(0.30, result.Iv30);
        else AssertUnavailable(result.Iv30);
    }

    [Fact]
    public void CustomAtmDistanceConfigurationIsUsed()
    {
        var configuration = Configuration with { ImpliedVolatility = new ImpliedVolatilityConfiguration { MaxAtmStrikeDistanceRatio = 0.10m } };

        AssertValue(0.30, Calculate([Chain(30, 0.20, 0.40, strike: 90m)], configuration: configuration).Iv30);
    }

    [Fact]
    public void CustomTargetDteConfigurationIsUsed()
    {
        var configuration = Configuration with { ImpliedVolatility = new ImpliedVolatilityConfiguration { TargetDteCalendarDays = 45 } };

        AssertValue(0.35, Calculate([Chain(45, 0.30, 0.40)], configuration: configuration).Iv30);
    }

    [Fact]
    public void SelectsStrikeBeforeCheckingForPairedIv()
    {
        var chain = Chain(30, 0.20, null, contracts:
        [Call(100m, 0.20), Put(100m, null), Call(101m, 0.40), Put(101m, 0.40)]);

        AssertUnavailable(Calculate([chain]).Iv30);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MissingEitherCallOrPutIvIsUnavailable(bool missingCall)
    {
        var result = Calculate([Chain(30, missingCall ? null : 0.20, missingCall ? 0.40 : null)]);

        AssertUnavailable(result.Iv30);
        Assert.Equal(Iv30UnavailableReason.NoEligibleAtmPair, result.Iv30UnavailableReason);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OneSidedIvIsUnavailable(bool callOnly)
    {
        var contracts = callOnly ? new[] { Call(100m, 0.30) } : [Put(100m, 0.30)];

        AssertUnavailable(Calculate([Chain(30, 0.30, 0.30, contracts: contracts)]).Iv30);
    }

    [Fact]
    public void ExactTargetExpirationPrecedesPossibleInterpolation()
    {
        var result = Calculate([Chain(20, 0.10, 0.10), Chain(30, 0.35, 0.35), Chain(40, 0.50, 0.50)]);

        AssertValue(0.35, result.Iv30);
    }

    [Fact]
    public void LatestAsOfSnapshotIsSelectedPerExpiration()
    {
        var earlier = Chain(30, 0.20, 0.20, observedAt: CalculatedAt.AddHours(-2));
        var later = Chain(30, 0.40, 0.40, observedAt: CalculatedAt.AddHours(-1));

        AssertValue(0.40, Calculate([later, earlier]).Iv30);
    }

    [Fact]
    public void NewerInvalidSnapshotMakesExpirationUnavailableWithoutUsingOlderValidSnapshot()
    {
        var earlier = Chain(30, 0.20, 0.40, observedAt: CalculatedAt.AddHours(-2));
        var laterWithoutPair = Chain(30, 0.90, null, observedAt: CalculatedAt.AddHours(-1));

        var result = Calculate([earlier, laterWithoutPair]);

        AssertUnavailable(result.Iv30);
        Assert.Equal(Iv30UnavailableReason.NoEligibleAtmPair, result.Iv30UnavailableReason);
    }

    [Fact]
    public void NewerEmptySnapshotMakesExpirationUnavailableWithoutUsingOlderValidSnapshot()
    {
        var earlier = Chain(30, 0.20, 0.40, observedAt: CalculatedAt.AddHours(-2));
        var laterEmpty = Chain(30, null, null, observedAt: CalculatedAt.AddHours(-1), contracts: []);

        var result = Calculate([laterEmpty, earlier]);

        AssertUnavailable(result.Iv30);
        Assert.Equal(Iv30UnavailableReason.NoEligibleAtmPair, result.Iv30UnavailableReason);
    }

    [Fact]
    public void NewerValidSnapshotReplacesOlderInvalidSnapshot()
    {
        var earlierWithoutPair = Chain(30, 0.20, null, observedAt: CalculatedAt.AddHours(-2));
        var later = Chain(30, 0.40, 0.60, observedAt: CalculatedAt.AddHours(-1));

        AssertValue(0.50, Calculate([later, earlierWithoutPair]).Iv30);
    }

    [Fact]
    public void NewerSnapshotAfterAsOfDateDoesNotReplaceLatestEligibleSnapshot()
    {
        var earlier = Chain(30, 0.20, 0.40, observedAt: CalculatedAt.AddHours(-1));
        var later = Chain(30, 0.90, 0.90, observedAt: CalculatedAt.AddDays(1));

        AssertValue(0.30, Calculate([later, earlier]).Iv30);
    }

    [Fact]
    public void NewerSameDaySnapshotAfterCalculatedAtDoesNotReplaceLatestEligibleSnapshot()
    {
        var earlier = Chain(30, 0.20, 0.40, observedAt: CalculatedAt.AddHours(-1));
        var later = Chain(30, 0.90, 0.90, observedAt: CalculatedAt.AddMinutes(1));

        AssertValue(0.30, Calculate([later, earlier]).Iv30);
    }

    [Fact]
    public void SelectsLatestEligibleSnapshotIndependentlyForEachInterpolationExpiration()
    {
        var chains = new[]
        {
            Chain(40, 0.60, 0.60, observedAt: CalculatedAt.AddHours(-1)),
            Chain(20, 0.10, 0.10, observedAt: CalculatedAt.AddHours(-2)),
            Chain(40, 0.30, 0.30, observedAt: CalculatedAt.AddHours(-2)),
            Chain(20, 0.20, 0.20, observedAt: CalculatedAt.AddHours(-1))
        };

        AssertValue(0.40, Calculate(chains).Iv30);
    }

    [Fact]
    public void FutureSnapshotIsExcludedAndAppendingItDoesNotChangeHistoricalResult()
    {
        var historical = Chain(30, 0.20, 0.40);
        var future = Chain(30, 0.90, 0.90, observedAt: CalculatedAt.AddDays(1));

        Assert.Equal(Calculate([historical]), Calculate([historical, future]));
        AssertUnavailable(Calculate([future]).Iv30);
    }

    [Fact]
    public void SameDaySnapshotObservedAfterCalculationTimeIsExcluded()
    {
        var laterSameDay = Chain(30, 0.90, 0.90, observedAt: CalculatedAt.AddMinutes(1));

        AssertUnavailable(Calculate([laterSameDay]).Iv30);
    }

    [Fact]
    public void ConflictingUnderlyingPricesDoNotProduceAnArbitraryAtmStrike()
    {
        var chain = Chain(30, 0.20, 0.40, contracts:
        [Call(100m, 0.20), Put(100m, 0.40) with { UnderlyingPrice = 101m }]);

        AssertUnavailable(Calculate([chain]).Iv30);
    }

    [Fact]
    public void MissingHistoricalOptionDataIsUnavailableRatherThanZero()
    {
        var result = Calculate([]);

        AssertUnavailable(result.Iv30);
        Assert.NotNull(result.Iv30UnavailableReason);
    }

    [Fact]
    public void LiquidityFieldsAreNotPartOfIvEligibility()
    {
        // The IV input intentionally contains only the normalized fields needed for this calculation.
        AssertValue(0.30, Calculate([Chain(30, 0.20, 0.40)]).Iv30);
    }

    [Fact]
    public void ExactlyOneHundredTwentySixValidIv30ObservationsMeetMinimum()
    {
        var result = Calculate([Chain(30, 0.40, 0.40)], History(125, index => index == 0 ? 0.20 : 0.30));

        AssertValue(100, result.IvRank);
        AssertValue(100d * 125 / 126, result.IvPercentile);
    }

    [Fact]
    public void OneHundredTwentyFiveValidIv30ObservationsAreInsufficient()
    {
        var result = Calculate([Chain(30, 0.40, 0.40)], History(124, _ => 0.20));

        AssertUnavailable(result.IvRank);
        AssertUnavailable(result.IvPercentile);
    }

    [Fact]
    public void UsesOnlyMostRecentTwoHundredFiftyTwoValidIv30Observations()
    {
        var values = Enumerable.Repeat(0.01, 9).Concat(Enumerable.Range(0, 251).Select(index => index % 2 == 0 ? 0.20 : 0.40)).ToArray();
        var result = Calculate([Chain(30, 0.30, 0.30)], History(values.Length, index => values[index]));

        AssertValue(50, result.IvRank);
        AssertValue(50, result.IvPercentile);
    }

    [Fact]
    public void MissingIv30ObservationsAreIgnoredWithoutConsumingLookbackSlots()
    {
        var values = Enumerable.Repeat<double?>(0.20, 125).Concat(Enumerable.Repeat<double?>(null, 20)).ToArray();
        var result = Calculate([Chain(30, 0.40, 0.40)], History(values.Length, index => values[index]));

        AssertValue(100, result.IvRank);
        AssertValue(100d * 125 / 126, result.IvPercentile);
    }

    [Fact]
    public void IvRankUsesFixedMinMaxReferenceValues()
    {
        var values = Enumerable.Repeat(0.30, 123).Concat([0.20, 0.40]).ToArray();
        var result = Calculate([Chain(30, 0.35, 0.35)], History(values.Length, index => values[index]));

        AssertValue(75, result.IvRank);
    }

    [Fact]
    public void EqualMinMaxLeavesRankUnavailableButPercentileAvailable()
    {
        var result = Calculate([Chain(30, 0.30, 0.30)], History(125, _ => 0.30));

        AssertUnavailable(result.IvRank);
        AssertValue(0, result.IvPercentile);
    }

    [Fact]
    public void PercentileCountsStrictlyBelowAndIncludesCurrentInDenominator()
    {
        var values = Enumerable.Repeat(0.20, 124).Append(0.30).ToArray();
        var result = Calculate([Chain(30, 0.30, 0.30)], History(values.Length, index => values[index]));

        AssertValue(100d * 124 / 126, result.IvPercentile);
    }

    [Fact]
    public void FutureHistoryDoesNotChangeRankOrPercentile()
    {
        var history = History(125, index => index == 0 ? 0.20 : 0.30);
        var future = new HistoricalIv30Observation("MSFT", AsOfDate.AddDays(1), IndicatorValue<double>.Available(0.99), CalculationVersion, Configuration.Version);

        Assert.Equal(Calculate([Chain(30, 0.40, 0.40)], history), Calculate([Chain(30, 0.40, 0.40)], history.Append(future)));
    }

    [Fact]
    public void UnavailableCurrentIv30MakesRankAndPercentileUnavailable()
    {
        var result = Calculate([], History(200, _ => 0.30));

        AssertUnavailable(result.Iv30);
        AssertUnavailable(result.IvRank);
        AssertUnavailable(result.IvPercentile);
    }

    [Fact]
    public void UnavailableReasonIsRetainedWhenIvContextIsAppliedToIndicatorSnapshot()
    {
        var baseSnapshot = new SimpleMovingAverageIndicatorCalculator().Calculate(new IndicatorCalculationRequest(
            "MSFT", AsOfDate, [], Configuration, CalculationVersion, CalculatedAt));
        var context = Calculate([]);

        var combined = context.ApplyTo(baseSnapshot);

        AssertUnavailable(combined.Iv30);
        Assert.Equal(Iv30UnavailableReason.NoEligibleSnapshot, combined.Iv30UnavailableReason);
    }

    [Fact]
    public void HistoricalContextDoesNotMixSymbolsOrCalculationVersions()
    {
        var history = History(124, _ => 0.20)
            .Append(new HistoricalIv30Observation("AAPL", AsOfDate.AddDays(-200), IndicatorValue<double>.Available(0.10), CalculationVersion, Configuration.Version))
            .Append(new HistoricalIv30Observation("MSFT", AsOfDate.AddDays(-201), IndicatorValue<double>.Available(0.10), new IndicatorCalculationVersion("older"), Configuration.Version));

        var result = Calculate([Chain(30, 0.40, 0.40)], history);

        AssertUnavailable(result.IvRank);
        AssertUnavailable(result.IvPercentile);
    }

    [Fact]
    public void RejectsInvalidIvConfiguration()
    {
        var configuration = Configuration with { ImpliedVolatility = new ImpliedVolatilityConfiguration { MinimumHistoricalValidObservations = 253 } };

        Assert.Throws<ArgumentOutOfRangeException>(() => Calculate([Chain(30, 0.20, 0.40)], configuration: configuration));
    }

    private static ImpliedVolatilityContext Calculate(
        IReadOnlyList<IvOptionChainObservation> chains,
        IEnumerable<HistoricalIv30Observation>? history = null,
        IndicatorConfiguration? configuration = null) =>
        Calculator.Calculate(new ImpliedVolatilityCalculationRequest("MSFT", AsOfDate, chains, (history ?? []).ToArray(),
            configuration ?? Configuration, CalculationVersion, CalculatedAt));

    private static IvOptionChainObservation Chain(int dte, double? callIv, double? putIv,
        decimal strike = 100m, DateTimeOffset? observedAt = null, IReadOnlyList<IvOptionContract>? contracts = null) =>
        new("MSFT", AsOfDate.AddDays(dte), observedAt ?? CalculatedAt.AddHours(-1),
            contracts ?? [Call(strike, callIv), Put(strike, putIv)]);

    private static IvOptionContract Call(decimal strike, double? iv) => new(strike, IvOptionType.Call, iv, 100m);
    private static IvOptionContract Put(decimal strike, double? iv) => new(strike, IvOptionType.Put, iv, 100m);

    private static HistoricalIv30Observation[] History(int count, Func<int, double?> value) =>
        Enumerable.Range(0, count).Select(index => new HistoricalIv30Observation("MSFT", AsOfDate.AddDays(index - count),
            value(index) is { } iv ? IndicatorValue<double>.Available(iv) : IndicatorValue<double>.InsufficientData(),
            CalculationVersion, Configuration.Version)).ToArray();

    private static void AssertValue(double expected, IndicatorValue<double> actual)
    {
        Assert.Equal(IndicatorValueStatus.Available, actual.Status);
        Assert.NotNull(actual.Value);
        Assert.InRange(actual.Value.Value, expected - 1e-10, expected + 1e-10);
    }

    private static void AssertUnavailable(IndicatorValue<double> actual)
    {
        Assert.Equal(IndicatorValueStatus.InsufficientData, actual.Status);
        Assert.Null(actual.Value);
    }
}
