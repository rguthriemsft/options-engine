using OptionsEngine.Strategy.EntryStrategy;
using OptionsEngine.Strategy.Indicators;

namespace OptionsEngine.Strategy.Tests;

public sealed class Phase4ConfigurationTablesTests
{
    private static readonly EntryStrategyConfiguration Defaults = new() { Version = new ConfigurationVersion(1) };

    [Fact]
    public void CcosDefaultsMatchEveryApprovedV1Table()
    {
        var c = Defaults.Ccos;
        Defaults.Validate();

        Assert.Equal(new double[] { 25, 15, 15, 15, 15, 15 },
            new double[] { c.VolatilityMaximumScore, c.RsiMaximumScore, c.BollingerMaximumScore,
                c.TrendMomentumMaximumScore, c.ResistanceStructureMaximumScore, c.MarketSectorRegimeMaximumScore });
        Assert.Equal(70, c.DefaultMinimumCcos);
        Assert.Equal(new double[] { 40, 55, 70, 80, 90 },
            new double[] { c.Classification.WeakMinimum, c.Classification.WatchMinimum, c.Classification.SellCandidateMinimum,
                c.Classification.StrongMinimum, c.Classification.ExceptionalMinimum });
        Assert.Equal((15d, 10d), (c.Volatility.IvPercentileMaximumScore, c.Volatility.Iv30ToRv30MaximumScore));
        AssertBands(c.Volatility.IvPercentile,
            [new(null, false, 20, false, 0), new(20, true, 30, false, 3), new(30, true, 40, false, 6),
                new(40, true, 50, false, 9), new(50, true, 60, false, 11), new(60, true, 70, true, 13),
                new(70, false, null, false, 15)]);
        AssertBands(c.Volatility.Iv30ToRv30,
            [new(null, false, .90, false, 0), new(.90, true, 1, false, 2), new(1, true, 1.10, false, 4),
                new(1.10, true, 1.20, false, 6), new(1.20, true, 1.35, true, 8), new(1.35, false, null, false, 10)]);
        AssertBands(c.Rsi,
            [new(null, false, 40, false, 0), new(40, true, 50, false, 2), new(50, true, 55, false, 5),
                new(55, true, 60, false, 8), new(60, true, 65, false, 11), new(65, true, 70, false, 15),
                new(70, true, 75, false, 13), new(75, true, 80, true, 9), new(80, false, null, false, 5)]);
        Assert.Equal((10d, 5d), (c.Bollinger.PercentBMaximumScore, c.Bollinger.BandwidthPoints));
        AssertBands(c.Bollinger.PercentB,
            [new(null, false, .40, false, 0), new(.40, true, .60, false, 2), new(.60, true, .75, false, 5),
                new(.75, true, .90, false, 8), new(.90, true, 1.05, false, 10), new(1.05, true, 1.15, true, 7),
                new(1.15, false, null, false, 3)]);
        Assert.Equal((5d, 5d, 5d), (c.TrendMomentum.Sma20AboveSma50Points,
            c.TrendMomentum.Sma50AboveSma200Points, c.TrendMomentum.PositiveMacdHistogramPoints));
        Assert.Equal((0d, 5d, 5d, 5d), (c.ResistanceStructure.NoQualifiedResistancePoints,
            c.ResistanceStructure.DistanceMaximumScore, c.ResistanceStructure.TouchMaximumScore,
            c.ResistanceStructure.RecencyMaximumScore));
        AssertBands(c.ResistanceStructure.DistancePercent,
            [new(null, false, .02, true, 5), new(.02, false, .05, true, 3), new(.05, false, null, false, 1)]);
        AssertIntegerBands(c.ResistanceStructure.TouchCount,
            [new(2, 2, 1), new(3, 3, 3), new(4, null, 5)]);
        AssertIntegerBands(c.ResistanceStructure.AgeTradingDays,
            [new(0, 20, 5), new(21, 60, 3), new(61, null, 1)]);
        Assert.Equal((8d, 7d), (c.MarketSectorRegime.MarketMaximumScore, c.MarketSectorRegime.SectorMaximumScore));
        Assert.Equal(new RegimeScorePoints(8, 4, 0), c.MarketSectorRegime.Market);
        Assert.Equal(new RegimeScorePoints(7, 3, 0), c.MarketSectorRegime.Sector);
        Assert.Equal((1.15, 75d, 0d), (c.BreakoutVeto.BollingerPercentBThresholdExclusive,
            c.BreakoutVeto.RsiThresholdInclusive, c.BreakoutVeto.MacdHistogramThresholdExclusive));
    }

    [Fact]
    public void ContractDefaultsMatchEveryApprovedV1Table()
    {
        var c = Defaults.ContractScore;
        var gate = Defaults.ContractEligibility;

        Assert.Equal(new double[] { 25, 20, 20, 10, 10, 10, 5 },
            new double[] { c.DeltaMaximumScore, c.StrikeSafetyMaximumScore, c.PremiumEfficiencyMaximumScore,
                c.DteEfficiencyMaximumScore, c.VolatilityEdgeMaximumScore, c.LiquidityMaximumScore,
                c.ThetaEfficiencyMaximumScore });
        Assert.Equal(80, c.DefaultMinimumContractScore);
        Assert.Equal(new double[] { 60, 70, 80, 90 },
            new double[] { c.Classification.WeakMinimum, c.Classification.AcceptableMinimum,
                c.Classification.GoodMinimum, c.Classification.ExcellentMinimum });
        Assert.Equal((20d, 25d, 10d), (c.Delta.BelowPreferredPoints, c.Delta.WithinPreferredPoints, c.Delta.AbovePreferredPoints));
        AssertBands(c.StrikeSafety,
            [new(0, false, .01, false, 0), new(.01, true, .02, false, 4), new(.02, true, .03, false, 8),
                new(.03, true, .04, false, 12), new(.04, true, .06, false, 15), new(.06, true, .08, true, 18),
                new(.08, false, null, false, 20)]);
        AssertBands(c.PremiumEfficiency,
            [new(1, true, 1.10, false, 0), new(1.10, true, 1.25, false, 5),
                new(1.25, true, 1.50, false, 10), new(1.50, true, 2, false, 15),
                new(2, true, null, false, 20)]);
        AssertIntegerBands(c.DteEfficiency,
            [new(14, 20, 5), new(21, 35, 10), new(36, 45, 8)]);
        AssertBands(c.VolatilityEdge,
            [new(null, false, .90, false, 0), new(.90, true, 1, false, 2), new(1, true, 1.10, false, 4),
                new(1.10, true, 1.20, false, 6), new(1.20, true, 1.35, true, 8),
                new(1.35, false, null, false, 10)]);
        Assert.Equal((5d, 5d), (c.Liquidity.SpreadMaximumScore, c.Liquidity.OpenInterestMaximumScore));
        AssertBands(c.Liquidity.SpreadPercent,
            [new(null, false, .05, true, 5), new(.05, false, .10, true, 4),
                new(.10, false, .15, true, 2), new(.15, false, .20, true, 1)]);
        AssertIntegerBands(c.Liquidity.OpenInterest,
            [new(100, 249, 1), new(250, 499, 2), new(500, 999, 3),
                new(1000, 1999, 4), new(2000, null, 5)]);
        AssertBands(c.ThetaEfficiency,
            [new(null, false, .01, false, 0), new(.01, true, .02, false, 1),
                new(.02, true, .03, false, 2), new(.03, true, .04, false, 3),
                new(.04, true, .05, false, 4), new(.05, true, null, false, 5)]);
        Assert.Equal((14, 45, .25, .20, 100L, .20), (gate.MinimumDte, gate.MaximumDte,
            gate.GlobalMaximumInitialDelta, gate.HighTaxMaximumDelta, gate.MinimumOpenInterest,
            gate.MaximumBidAskSpreadPercent));
    }

    [Fact]
    public void InvalidContinuousBandsAndPointsFailClearly()
    {
        var original = Defaults.Ccos.Volatility.IvPercentile.Bands;
        Assert.Throws<ArgumentException>(() => (Defaults with { Ccos = Defaults.Ccos with
        {
            Volatility = Defaults.Ccos.Volatility with { IvPercentile = new ContinuousScoreTable
            {
                Bands = original.SetItem(1, original[1] with { Minimum = 21 })
            } }
        } }).Validate());
        Assert.Throws<ArgumentException>(() => (Defaults with { Ccos = Defaults.Ccos with
        {
            Volatility = Defaults.Ccos.Volatility with { IvPercentile = new ContinuousScoreTable
            {
                Bands = original.SetItem(1, original[1] with { IncludesMinimum = false })
            } }
        } }).Validate());
        Assert.Throws<ArgumentException>(() => (Defaults with { Ccos = Defaults.Ccos with
        {
            Volatility = Defaults.Ccos.Volatility with { IvPercentile = new ContinuousScoreTable
            {
                Bands = original.SetItem(1, original[1] with { Maximum = double.PositiveInfinity })
            } }
        } }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (Defaults with { Ccos = Defaults.Ccos with
        {
            Volatility = Defaults.Ccos.Volatility with { IvPercentile = new ContinuousScoreTable
            {
                Bands = original.SetItem(1, original[1] with { Points = double.NaN })
            } }
        } }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (Defaults with { ContractScore = Defaults.ContractScore with
        {
            Delta = Defaults.ContractScore.Delta with { AbovePreferredPoints = 26 }
        } }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (Defaults with { Ccos = Defaults.Ccos with
        {
            TrendMomentum = Defaults.Ccos.TrendMomentum with { PositiveMacdHistogramPoints = -1 }
        } }).Validate());
    }

    [Fact]
    public void InvalidIntegerBandsAndClassificationFailClearly()
    {
        var dte = Defaults.ContractScore.DteEfficiency.Bands;
        Assert.Throws<ArgumentException>(() => (Defaults with { ContractScore = Defaults.ContractScore with
        {
            DteEfficiency = new IntegerScoreTable { Bands = dte.SetItem(1, dte[1] with { Minimum = 22 }) }
        } }).Validate());
        Assert.Throws<ArgumentException>(() => (Defaults with { Ccos = Defaults.Ccos with
        {
            Classification = new CcosClassificationConfiguration { WatchMinimum = 40 }
        } }).Validate());
        Assert.Throws<ArgumentException>(() => (Defaults with { ContractScore = Defaults.ContractScore with
        {
            Classification = new ContractScoreClassificationConfiguration { ExcellentMinimum = double.PositiveInfinity }
        } }).Validate());
    }

    [Fact]
    public void ScoringTablesMustCoverConfiguredEligibilityLimits()
    {
        Assert.Throws<ArgumentException>(() => (Defaults with
        {
            ContractEligibility = Defaults.ContractEligibility with { MaximumDte = 46 }
        }).Validate());
        Assert.Throws<ArgumentException>(() => (Defaults with
        {
            ContractEligibility = Defaults.ContractEligibility with { MaximumBidAskSpreadPercent = .25 }
        }).Validate());
        Assert.Throws<ArgumentException>(() => (Defaults with
        {
            ContractEligibility = Defaults.ContractEligibility with { MinimumOpenInterest = 101 }
        }).Validate());
    }

    [Fact]
    public void ConsistentVersionedBandChangesCanBeValidated()
    {
        var bands = Defaults.Ccos.Rsi.Bands;
        var updated = new ContinuousScoreTable { Bands = bands
            .SetItem(0, bands[0] with { Maximum = 41 })
            .SetItem(1, bands[1] with { Minimum = 41, Points = 3 }) };

        (Defaults with { Ccos = Defaults.Ccos with { Rsi = updated } }).Validate();
    }

    private static void AssertBands(ContinuousScoreTable actual, ContinuousScoreBand[] expected) =>
        Assert.Equal(expected, actual.Bands.ToArray());

    private static void AssertIntegerBands(IntegerScoreTable actual, IntegerScoreBand[] expected) =>
        Assert.Equal(expected, actual.Bands.ToArray());
}
