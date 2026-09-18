using OptionsEngine.Domain.Accounts;
using OptionsEngine.Strategy.EntryStrategy;
using OptionsEngine.Strategy.Indicators;

namespace OptionsEngine.Strategy.Tests;

public sealed class CcosCalculatorTests
{
    private static readonly CcosCalculator Calculator = new();

    [Theory]
    [MemberData(nameof(IvPercentileCases))]
    public void IvPercentileBoundariesScoreExactly(double value, double expected)
    {
        var result = Evaluate(Indicators() with { IvPercentile = Available(value), Iv30 = Available(0d), RealizedVolatility30 = Available(1d) });

        Assert.Equal(expected, Component(result, ScoreComponentCode.CcosVolatility).Score);
    }

    [Theory]
    [MemberData(nameof(Iv30ToRv30Cases))]
    public void Iv30ToRv30BoundariesScoreExactly(double value, double expected)
    {
        var result = Evaluate(Indicators() with { IvPercentile = Available(0d), Iv30 = Available(value), RealizedVolatility30 = Available(1d) });

        Assert.Equal(expected, Component(result, ScoreComponentCode.CcosVolatility).Score);
    }

    [Theory]
    [MemberData(nameof(RsiCases))]
    public void RsiBoundariesScoreExactly(double value, double expected)
    {
        var result = Evaluate(Indicators() with { Rsi14 = Available(value) });

        Assert.Equal(expected, Component(result, ScoreComponentCode.CcosRsi).Score);
    }

    [Theory]
    [MemberData(nameof(BollingerCases))]
    public void BollingerPercentBBoundariesScoreExactly(double value, double expected)
    {
        var result = Evaluate(Indicators() with { BollingerPercentB = Available(value) });

        Assert.Equal(expected, Component(result, ScoreComponentCode.CcosBollinger).Score);
    }

    [Fact]
    public void TrendMomentumEqualityReceivesZeroForTheApplicableCondition()
    {
        Assert.Equal(10, Component(Evaluate(Indicators() with { Sma20 = Available(2m), Sma50 = Available(2m) }), ScoreComponentCode.CcosTrendMomentum).Score);
        Assert.Equal(10, Component(Evaluate(Indicators() with { Sma50 = Available(1m), Sma200 = Available(1m) }), ScoreComponentCode.CcosTrendMomentum).Score);
        Assert.Equal(10, Component(Evaluate(Indicators() with { MacdHistogram = Available(0d) }), ScoreComponentCode.CcosTrendMomentum).Score);
        Assert.Equal(15, Component(Evaluate(Indicators()), ScoreComponentCode.CcosTrendMomentum).Score);
    }

    [Theory]
    [MemberData(nameof(ResistanceCases))]
    public void ResistanceBoundariesAndOneTouchClarificationScoreExactly(double distance, int touches, int age, double expected)
    {
        var result = Evaluate(Indicators() with
        {
            DistanceToResistancePercent = Available(distance),
            ResistanceTouchCount = Available(touches),
            ResistanceAgeTradingDays = Available(age),
            ResistanceUnavailableReason = null
        });

        Assert.Equal(expected, Component(result, ScoreComponentCode.CcosResistanceStructure).Score);
    }

    [Fact]
    public void ResistanceStatesRemainDistinct()
    {
        var noQualified = Component(Evaluate(Indicators() with { ResistanceUnavailableReason = ResistanceUnavailableReason.NoQualifiedResistance }),
            ScoreComponentCode.CcosResistanceStructure);
        var insufficient = Component(Evaluate(Indicators() with { ResistanceUnavailableReason = ResistanceUnavailableReason.InsufficientData }),
            ScoreComponentCode.CcosResistanceStructure);

        Assert.Equal(ScoreStatus.Available, noQualified.Status);
        Assert.Equal(0, noQualified.Score);
        Assert.Equal(ScoreStatus.Unavailable, insufficient.Status);
        Assert.Null(insufficient.Score);
        Assert.Contains(MissingInputCode.ResistanceStatus, insufficient.MissingInputs);
    }

    [Theory]
    [InlineData(MarketRegime.Neutral, MarketRegime.Neutral, 15)]
    [InlineData(MarketRegime.Neutral, MarketRegime.Bullish, 11)]
    [InlineData(MarketRegime.Neutral, MarketRegime.Bearish, 8)]
    [InlineData(MarketRegime.Bullish, MarketRegime.Neutral, 11)]
    [InlineData(MarketRegime.Bullish, MarketRegime.Bullish, 7)]
    [InlineData(MarketRegime.Bullish, MarketRegime.Bearish, 4)]
    [InlineData(MarketRegime.Bearish, MarketRegime.Neutral, 7)]
    [InlineData(MarketRegime.Bearish, MarketRegime.Bullish, 3)]
    [InlineData(MarketRegime.Bearish, MarketRegime.Bearish, 0)]
    public void EveryAvailableMarketSectorCombinationScoresExactly(MarketRegime market, MarketRegime sector, double expected)
    {
        var result = Evaluate(Indicators() with { MarketRegime = market, SectorRegime = sector });

        Assert.Equal(expected, Component(result, ScoreComponentCode.CcosMarketSectorRegime).Score);
    }

    [Theory]
    [MemberData(nameof(MissingComponentCases))]
    public void MissingRequiredInputsMakeTheirComponentAndOverallCcosUnavailable(IndicatorContext indicators, ScoreComponentCode component,
        MissingInputCode expectedMissing)
    {
        var result = Evaluate(indicators);

        var item = Component(result, component);
        Assert.Equal(ScoreStatus.Unavailable, item.Status);
        Assert.Null(item.Score);
        Assert.Contains(expectedMissing, item.MissingInputs);
        Assert.Equal(ScoreStatus.Unavailable, result.Ccos.Status);
        Assert.Null(result.Ccos.Score);
        Assert.Contains(expectedMissing, result.Ccos.MissingInputs);
    }

    [Fact]
    public void NoWeightRenormalizationOccursWhenAComponentIsUnavailable()
    {
        var result = Evaluate(Indicators() with { Rsi14 = Missing<double>() });

        Assert.Equal(ScoreStatus.Unavailable, result.Ccos.Status);
        Assert.Null(result.Ccos.Score);
        Assert.Equal(ScoreStatus.Available, Component(result, ScoreComponentCode.CcosVolatility).Status);
    }

    [Theory]
    [InlineData(-0.0001)]
    [InlineData(100.0001)]
    public void RsiOutsideItsApprovedRangeIsUnavailable(double rsi)
    {
        var result = Evaluate(Indicators() with { Rsi14 = Available(rsi) });

        Assert.Equal(ScoreStatus.Unavailable, Component(result, ScoreComponentCode.CcosRsi).Status);
        Assert.Contains(MissingInputCode.Rsi14, result.Ccos.MissingInputs);
        var input = Assert.Single(Component(result, ScoreComponentCode.CcosRsi).Inputs, x => x.Code == "RSI14");
        Assert.Equal(AvailabilityStatus.Unavailable, input.Status);
        Assert.Equal(rsi.ToString("G17", System.Globalization.CultureInfo.InvariantCulture), input.Value);
    }

    [Theory]
    [InlineData(-0.0001)]
    [InlineData(100.0001)]
    public void RsiOutsideItsApprovedRangeMakesBreakoutVetoUnavailable(double rsi)
    {
        var result = Evaluate(Indicators() with { BollingerPercentB = Available(1.16), Rsi14 = Available(rsi), MacdHistogram = Available(.01) });

        Assert.Equal(GateStatus.Unavailable, result.BreakoutVeto.Status);
        Assert.Equal(RejectionReasonCode.InsufficientData, result.BreakoutVeto.ReasonCode);
        Assert.Contains(MissingInputCode.Rsi14, result.BreakoutVeto.MissingInputs);
        var input = Assert.Single(result.BreakoutVeto.Inputs, x => x.Code == "RSI14");
        Assert.Equal(AvailabilityStatus.Unavailable, input.Status);
        Assert.Equal(rsi.ToString("G17", System.Globalization.CultureInfo.InvariantCulture), input.Value);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-0.0001d)]
    public void InvalidRv30HasOneUnavailableInputWithItsSuppliedValue(double rv30)
    {
        var result = Evaluate(Indicators() with { RealizedVolatility30 = Available(rv30) });
        var input = Assert.Single(Component(result, ScoreComponentCode.CcosVolatility).Inputs, x => x.Code == "RV30");

        Assert.Equal(AvailabilityStatus.Unavailable, input.Status);
        Assert.Equal(rv30.ToString("G17", System.Globalization.CultureInfo.InvariantCulture), input.Value);
    }

    [Theory]
    [MemberData(nameof(ClassificationCases))]
    public void ClassificationBoundariesAreDeterministic(double score, CcosClassification expected)
    {
        Assert.Equal(expected, CcosCalculator.Classify(score, new CcosClassificationConfiguration()));
    }

    [Fact]
    public void CcosComparesAgainstTheSnapshottedHoldingMinimum()
    {
        var atMinimum = Evaluate(Indicators(), minimumCcos: 96);
        var aboveMinimum = Evaluate(Indicators(), minimumCcos: 96.1);

        Assert.Equal(96, atMinimum.Ccos.Score);
        Assert.True(atMinimum.Ccos.MeetsConfiguredMinimum);
        Assert.False(aboveMinimum.Ccos.MeetsConfiguredMinimum);
    }

    [Theory]
    [InlineData(1.1499, 75, .01, GateStatus.Passed)]
    [InlineData(1.15, 75, .01, GateStatus.Passed)]
    [InlineData(1.1501, 75, .01, GateStatus.Failed)]
    [InlineData(1.16, 74.999, .01, GateStatus.Passed)]
    [InlineData(1.16, 75, .01, GateStatus.Failed)]
    [InlineData(1.16, 75, -0.0001, GateStatus.Passed)]
    [InlineData(1.16, 75, 0, GateStatus.Passed)]
    [InlineData(1.16, 75, .0001, GateStatus.Failed)]
    public void BreakoutVetoUsesExactlyTheApprovedBoundaries(double percentB, double rsi, double macd, GateStatus expected)
    {
        var result = Evaluate(Indicators() with { BollingerPercentB = Available(percentB), Rsi14 = Available(rsi), MacdHistogram = Available(macd) });

        Assert.Equal(expected, result.BreakoutVeto.Status);
        Assert.Equal(expected == GateStatus.Failed ? RejectionReasonCode.BreakoutVeto : null, result.BreakoutVeto.ReasonCode);
    }

    [Fact]
    public void BreakoutVetoRetainsAnAvailableCcosAndMissingGateInputIsExplicit()
    {
        var veto = Evaluate(Indicators() with { BollingerPercentB = Available(1.16), Rsi14 = Available(75d), MacdHistogram = Available(.01) });
        var unavailable = Evaluate(Indicators() with { MacdHistogram = Missing<double>() });

        Assert.Equal(ScoreStatus.Available, veto.Ccos.Status);
        Assert.Equal(GateStatus.Failed, veto.BreakoutVeto.Status);
        Assert.Equal(GateStatus.Unavailable, unavailable.BreakoutVeto.Status);
        Assert.Contains(MissingInputCode.MacdHistogram, unavailable.BreakoutVeto.MissingInputs);
    }

    [Fact]
    public void BreakoutVetoGoldenScenarioRetainsCcosForExplanation()
    {
        var result = Evaluate(Indicators() with { BollingerPercentB = Available(1.16), Rsi14 = Available(75d), MacdHistogram = Available(.01) });

        Assert.Equal(ScoreStatus.Available, result.Ccos.Status);
        Assert.Equal(GateStatus.Failed, result.BreakoutVeto.Status);
        Assert.Equal(RejectionReasonCode.BreakoutVeto, result.BreakoutVeto.ReasonCode);
    }

    [Fact]
    public void InsufficientDataGoldenScenarioReportsExactMissingInputWithoutAZeroScore()
    {
        var result = Evaluate(Indicators() with { RealizedVolatility30 = Missing<double>() });

        Assert.Equal(ScoreStatus.Unavailable, result.Ccos.Status);
        Assert.Null(result.Ccos.Score);
        Assert.Contains(MissingInputCode.RealizedVolatility30, result.Ccos.MissingInputs);
        Assert.Equal(GateStatus.Passed, result.BreakoutVeto.Status);
    }

    public static IEnumerable<object[]> IvPercentileCases() => BoundaryCases(
        [(20d, 0d, 3d, 3d), (30d, 3d, 6d, 6d), (40d, 6d, 9d, 9d), (50d, 9d, 11d, 11d), (60d, 11d, 13d, 13d), (70d, 13d, 13d, 15d)]);
    public static IEnumerable<object[]> Iv30ToRv30Cases() => BoundaryCases(
        [(.90d, 0d, 2d, 2d), (1d, 2d, 4d, 4d), (1.10d, 4d, 6d, 6d), (1.20d, 6d, 8d, 8d), (1.35d, 8d, 8d, 10d)]);
    public static IEnumerable<object[]> RsiCases() => BoundaryCases(
        [(40d, 0d, 2d, 2d), (50d, 2d, 5d, 5d), (55d, 5d, 8d, 8d), (60d, 8d, 11d, 11d), (65d, 11d, 15d, 15d), (70d, 15d, 13d, 13d), (75d, 13d, 9d, 9d), (80d, 9d, 9d, 5d)]);
    public static IEnumerable<object[]> BollingerCases() => BoundaryCases(
        [(.40d, 5d, 7d, 7d), (.60d, 7d, 10d, 10d), (.75d, 10d, 13d, 13d), (.90d, 13d, 15d, 15d), (1.05d, 15d, 12d, 12d), (1.15d, 12d, 12d, 8d)]);
    public static IEnumerable<object[]> ClassificationCases() =>
        new[]
        {
            new object[] { 39.999d, CcosClassification.NoTrade }, new object[] { 40d, CcosClassification.Weak }, new object[] { 40.0001d, CcosClassification.Weak },
            new object[] { 54.999d, CcosClassification.Weak }, new object[] { 55d, CcosClassification.Watch },
            new object[] { 55.0001d, CcosClassification.Watch }, new object[] { 69.999d, CcosClassification.Watch }, new object[] { 70d, CcosClassification.SellCandidate },
            new object[] { 70.0001d, CcosClassification.SellCandidate }, new object[] { 79.999d, CcosClassification.SellCandidate }, new object[] { 80d, CcosClassification.Strong },
            new object[] { 80.0001d, CcosClassification.Strong }, new object[] { 89.999d, CcosClassification.Strong }, new object[] { 90d, CcosClassification.Exceptional },
            new object[] { 90.0001d, CcosClassification.Exceptional }
        };

    public static IEnumerable<object[]> ResistanceCases() =>
        new[]
        {
            new object[] { .0199d, 1, 20, 10d }, new object[] { .0201d, 1, 20, 8d },
            new object[] { .0499d, 1, 20, 8d }, new object[] { .05d, 1, 20, 8d }, new object[] { .0501d, 1, 20, 6d },
            new object[] { .02d, 1, 20, 10d }, new object[] { .02d, 2, 20, 11d }, new object[] { .02d, 3, 20, 13d },
            new object[] { .02d, 4, 20, 15d }, new object[] { .02d, 5, 20, 15d },
            new object[] { .02d, 4, 19, 15d }, new object[] { .02d, 4, 21, 13d },
            new object[] { .02d, 4, 59, 13d }, new object[] { .02d, 4, 60, 13d }, new object[] { .02d, 4, 61, 11d }
        };

    public static IEnumerable<object[]> MissingComponentCases()
    {
        yield return [Indicators() with { Iv30 = Missing<double>() }, ScoreComponentCode.CcosVolatility, MissingInputCode.Iv30];
        yield return [Indicators() with { IvPercentile = Missing<double>() }, ScoreComponentCode.CcosVolatility, MissingInputCode.IvPercentile];
        yield return [Indicators() with { RealizedVolatility30 = Missing<double>() }, ScoreComponentCode.CcosVolatility, MissingInputCode.RealizedVolatility30];
        yield return [Indicators() with { Rsi14 = Missing<double>() }, ScoreComponentCode.CcosRsi, MissingInputCode.Rsi14];
        yield return [Indicators() with { BollingerPercentB = Missing<double>() }, ScoreComponentCode.CcosBollinger, MissingInputCode.BollingerPercentB];
        yield return [Indicators() with { BollingerBandwidth = Missing<double>() }, ScoreComponentCode.CcosBollinger, MissingInputCode.BollingerBandwidth];
        yield return [Indicators() with { Sma20 = Missing<decimal>() }, ScoreComponentCode.CcosTrendMomentum, MissingInputCode.Sma20];
        yield return [Indicators() with { Sma50 = Missing<decimal>() }, ScoreComponentCode.CcosTrendMomentum, MissingInputCode.Sma50];
        yield return [Indicators() with { Sma200 = Missing<decimal>() }, ScoreComponentCode.CcosTrendMomentum, MissingInputCode.Sma200];
        yield return [Indicators() with { MacdHistogram = Missing<double>() }, ScoreComponentCode.CcosTrendMomentum, MissingInputCode.MacdHistogram];
        yield return [Indicators() with { ResistanceUnavailableReason = ResistanceUnavailableReason.InsufficientData }, ScoreComponentCode.CcosResistanceStructure, MissingInputCode.ResistanceStatus];
        yield return [Indicators() with { DistanceToResistancePercent = Missing<double>(), ResistanceUnavailableReason = null }, ScoreComponentCode.CcosResistanceStructure, MissingInputCode.ResistanceDistancePercent];
        yield return [Indicators() with { ResistanceTouchCount = Missing<int>(), ResistanceUnavailableReason = null }, ScoreComponentCode.CcosResistanceStructure, MissingInputCode.ResistanceTouchCount];
        yield return [Indicators() with { ResistanceAgeTradingDays = Missing<int>(), ResistanceUnavailableReason = null }, ScoreComponentCode.CcosResistanceStructure, MissingInputCode.ResistanceAgeTradingDays];
        yield return [Indicators() with { RealizedVolatility30 = Available(0d) }, ScoreComponentCode.CcosVolatility, MissingInputCode.RealizedVolatility30];
        yield return [Indicators() with { RealizedVolatility30 = Available(-0.0001d) }, ScoreComponentCode.CcosVolatility, MissingInputCode.RealizedVolatility30];
        yield return [Indicators() with { MarketRegime = MarketRegime.InsufficientData }, ScoreComponentCode.CcosMarketSectorRegime, MissingInputCode.MarketRegime];
        yield return [Indicators() with { SectorRegime = MarketRegime.InsufficientData }, ScoreComponentCode.CcosMarketSectorRegime, MissingInputCode.SectorRegime];
    }

    private static IEnumerable<object[]> BoundaryCases((double Boundary, double Below, double At, double Above)[] cases)
    {
        foreach (var (boundary, below, at, above) in cases)
        {
            yield return [boundary - .0001d, below];
            yield return [boundary, at];
            yield return [boundary + .0001d, above];
        }
    }

    private static UnderlyingEligibilityResult Evaluate(IndicatorContext indicators, double minimumCcos = 70) =>
        Calculator.Evaluate(new EvaluationContext(Holding(minimumCcos), indicators, new EarningsContext(AvailabilityStatus.NotApplicable, null),
            indicators.AsOfDate, new DateTimeOffset(2026, 9, 18, 15, 0, 0, TimeSpan.Zero),
            new EntryStrategyConfiguration { Version = new ConfigurationVersion(1) }, new StrategyVersion("1.0.0")));

    private static HoldingContext Holding(double minimumCcos) => new(Guid.Parse("1B539C36-CA1A-4FB0-B34A-84C821258AAB"), "MSFT",
        AssetType.Stock, AssignmentSensitivity.Level3, TaxSensitivity.Moderate, .25, .12, .18, minimumCcos, 80, .10m, .10);

    private static IndicatorContext Indicators() => new("MSFT", new DateOnly(2026, 9, 17),
        Available(1d), Available(50d), Available(.5d), Available(65d), Available(.95d), Available(.1d),
        Available(3m), Available(2m), Available(1m), Available(1d), Available(105m), Available(.02d), Available(4), Available(20), null,
        MarketRegime.Neutral, MarketRegime.Neutral, new IndicatorCalculationVersion("3.0.0"), new ConfigurationVersion(1),
        new DateTimeOffset(2026, 9, 18, 1, 0, 0, TimeSpan.Zero));

    private static ScoreComponentResult Component(UnderlyingEligibilityResult result, ScoreComponentCode code) =>
        result.Ccos.Components.Single(x => x.Code == code);

    private static IndicatorValue<T> Available<T>(T value) where T : struct => IndicatorValue<T>.Available(value);
    private static IndicatorValue<T> Missing<T>() where T : struct => IndicatorValue<T>.InsufficientData();
}
