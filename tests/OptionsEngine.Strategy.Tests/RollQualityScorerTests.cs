using OptionsEngine.Strategy.Defense;
using OptionsEngine.Strategy.EntryStrategy;
using OptionsEngine.Strategy.Indicators;

namespace OptionsEngine.Strategy.Tests;

public sealed class RollQualityScorerTests
{
    private readonly RollQualityScorer scorer = new();

    [Theory]
    [InlineData(80, 40, .50, 15)]
    [InlineData(80, 20, .75, 22.5)]
    public void DrsReductionUsesApprovedRatio(double current, double projected,
        double expectedRatio, double expectedScore)
    {
        var component = Component(Evaluate(Input(currentDrs: current, projectedDrs: projected)),
            RqsComponentCode.DrsReduction);

        Assert.Equal(expectedRatio, component.DerivedValues["DrsReductionRatio"]!.Value, 10);
        Assert.Equal(expectedScore, component.Score!.Value, 10);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveCurrentDrsMakesComponentUnavailable(double current)
    {
        var component = Component(Evaluate(Input(currentDrs: current)), RqsComponentCode.DrsReduction);

        Assert.Equal(EvaluationValueStatus.InsufficientData, component.Status);
        Assert.Null(component.Score);
        Assert.Contains(RollMissingInputCode.CurrentDrs, component.MissingInputs);
    }

    [Theory]
    [InlineData(.40, .20, .50, 10)]
    [InlineData(.40, .10, .75, 15)]
    public void DeltaReductionUsesApprovedRatio(double current, double replacement,
        double expectedRatio, double expectedScore)
    {
        var component = Component(Evaluate(Input(currentDelta: current, newDelta: replacement)),
            RqsComponentCode.DeltaReduction);

        Assert.Equal(expectedRatio, component.DerivedValues["DeltaReductionRatio"]!.Value, 10);
        Assert.Equal(expectedScore, component.Score!.Value, 10);
    }

    [Fact]
    public void ZeroCurrentDeltaMakesComponentUnavailable()
    {
        var component = Component(Evaluate(Input(currentDelta: 0)), RqsComponentCode.DeltaReduction);

        Assert.Equal(EvaluationValueStatus.InsufficientData, component.Status);
        Assert.Contains(RollMissingInputCode.ExistingDelta, component.MissingInputs);
    }

    [Theory]
    [InlineData(103, .03, 6)]
    [InlineData(105, .05, 10)]
    [InlineData(110, .10, 20)]
    [InlineData(115, .15, 20)]
    public void StrikeImprovementSaturatesAtConfiguredFullRatio(decimal replacementStrike,
        double expectedRatio, double expectedScore)
    {
        var component = Component(Evaluate(Input(replacementStrike: replacementStrike)),
            RqsComponentCode.StrikeImprovement);

        Assert.Equal(expectedRatio, component.DerivedValues["StrikeImprovementRatio"]!.Value, 10);
        Assert.Equal(expectedScore, component.Score!.Value, 10);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(.25, 7.5)]
    [InlineData(.50, 5)]
    [InlineData(.75, 2.5)]
    [InlineData(1, 0)]
    public void DebitEconomicsUsesMaximumDebitUtilization(double utilization, double expectedScore)
    {
        var debit = (decimal)utilization;
        var component = Component(Evaluate(Input(netRoll: -debit, rollDebit: debit)),
            RqsComponentCode.RollEconomics);

        Assert.Equal(utilization, component.DerivedValues["DebitUtilization"]!.Value, 10);
        Assert.Equal(expectedScore, component.Score!.Value, 10);
    }

    [Fact]
    public void DebitWithZeroMaximumIsUnavailableWithoutDivisionByZero()
    {
        var component = Component(Evaluate(Input(netRoll: -.01m, rollDebit: .01m,
            configuration: Roll(0))), RqsComponentCode.RollEconomics);

        Assert.Equal(EvaluationValueStatus.InsufficientData, component.Status);
        Assert.Null(component.Score);
        Assert.Contains(RollMissingInputCode.Configuration, component.MissingInputs);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(.10, 12)]
    [InlineData(.25, 15)]
    [InlineData(.30, 15)]
    public void EvenAndCreditEconomicsAreContinuousAndSaturate(double creditRatio,
        double expectedScore)
    {
        var net = (decimal)creditRatio;
        var component = Component(Evaluate(Input(netRoll: net, rollDebit: 0,
            replacementSto: 1m)), RqsComponentCode.RollEconomics);

        Assert.Equal(expectedScore, component.Score!.Value, 10);
        if (creditRatio > 0)
            Assert.Equal(creditRatio, component.DerivedValues["CreditRatio"]!.Value, 10);
    }

    [Fact]
    public void ReplacementLiquidityReusesPhaseFourScore()
    {
        var component = Component(Evaluate(Input(candidateBid: .975m, candidateAsk: 1.025m,
            openInterest: 500)), RqsComponentCode.ReplacementLiquidity);

        Assert.Equal(8, component.Score);
        Assert.Equal(5, component.DerivedValues["SpreadScore"]);
        Assert.Equal(3, component.DerivedValues["OpenInterestScore"]);
    }

    [Fact]
    public void ReplacementLiquidityHonorsNonDefaultSharedScoringConfiguration()
    {
        var configuration = Roll(1) with
        {
            LiquidityEligibility = new ContractLiquidityEligibilityConfiguration(250, .15),
            LiquidityScoring = new ContractLiquidityScoringConfiguration
            {
                SpreadPercent = new ContinuousScoreTable { Bands =
                [
                    new(null, false, .04, true, 5), new(.04, false, .08, true, 4),
                    new(.08, false, .12, true, 2), new(.12, false, .15, true, 1)
                ] },
                OpenInterest = new IntegerScoreTable { Bands =
                [
                    new(250, 499, 1), new(500, 999, 2), new(1000, 1999, 3),
                    new(2000, 3999, 4), new(4000, null, 5)
                ] }
            }
        };
        var component = Component(Evaluate(Input(candidateBid: .96m, candidateAsk: 1.04m,
            openInterest: 500, configuration: configuration)), RqsComponentCode.ReplacementLiquidity);

        Assert.Equal(6, component.Score);
        Assert.Equal(4, component.DerivedValues["SpreadScore"]);
        Assert.Equal(2, component.DerivedValues["OpenInterestScore"]);
    }

    [Theory]
    [InlineData(21, 1, 5)]
    [InlineData(30, .7692307692307693, 3.8461538461538463)]
    [InlineData(45, .38461538461538464, 1.9230769230769231)]
    [InlineData(60, 0, 0)]
    public void TimeEfficiencyUsesTwentyOneToSixtyDte(int dte, double expectedRatio,
        double expectedScore)
    {
        var component = Component(Evaluate(Input(newDte: dte)), RqsComponentCode.TimeEfficiency);

        Assert.Equal(expectedRatio, component.DerivedValues["TimeEfficiencyRatio"]!.Value, 12);
        Assert.Equal(expectedScore, component.Score!.Value, 12);
    }

    [Fact]
    public void CompleteComponentsProduceAvailableBoundedTotal()
    {
        var result = Evaluate(Input());

        Assert.Equal(EvaluationValueStatus.Available, result.Status);
        Assert.NotNull(result.Score);
        Assert.InRange(result.Score.Value, 0, 100);
        Assert.All(result.Components, component =>
        {
            Assert.Equal(EvaluationValueStatus.Available, component.Status);
            Assert.InRange(component.Score!.Value, 0, component.MaximumScore);
        });
    }

    [Fact]
    public void MaximumComponentValuesProduceRqsOneHundred()
    {
        var result = Evaluate(Input(currentDrs: 100, projectedDrs: 0,
            currentDelta: 1, newDelta: 0, replacementStrike: 200,
            netRoll: .25m, rollDebit: 0, replacementSto: 1m,
            candidateBid: .975m, candidateAsk: 1.025m, openInterest: 2000, newDte: 21));

        Assert.Equal(EvaluationValueStatus.Available, result.Status);
        Assert.Equal(100, result.Score!.Value, 10);
    }

    [Fact]
    public void MissingOneComponentMakesTotalUnavailableWithoutDiscardingAvailableComponents()
    {
        var result = Evaluate(Input(candidateAsk: null));

        Assert.Equal(EvaluationValueStatus.InsufficientData, result.Status);
        Assert.Null(result.Score);
        Assert.Contains(RollMissingInputCode.CandidateAsk, result.MissingInputs);
        Assert.Equal(EvaluationValueStatus.InsufficientData,
            Component(result, RqsComponentCode.ReplacementLiquidity).Status);
        Assert.Equal(5, result.Components.Count(component =>
            component.Status == EvaluationValueStatus.Available));
    }

    private RqsResult Evaluate(RollQualityScoringInput input) => scorer.Evaluate(input);

    private static RqsComponentResult Component(RqsResult result, RqsComponentCode code) =>
        Assert.Single(result.Components, component => component.Code == code);

    private static RollQualityScoringInput Input(double? currentDrs = 80, double? projectedDrs = 20,
        double? currentDelta = .40, double? newDelta = .20, decimal? existingStrike = 100,
        decimal? replacementStrike = 110, decimal? netRoll = 0, decimal? rollDebit = 0,
        decimal? replacementSto = 1, decimal? candidateBid = .95m, decimal? candidateAsk = 1.05m,
        long? openInterest = 500, int? newDte = 30, RollConfiguration? configuration = null) =>
        new(currentDrs, projectedDrs, currentDelta, newDelta, existingStrike, replacementStrike,
            netRoll, rollDebit, replacementSto, candidateBid, candidateAsk, openInterest, newDte,
            configuration ?? Roll(1));

    private static RollConfiguration Roll(decimal maximumDebit) => new()
    {
        Version = new ConfigurationVersion(1),
        MaximumRollDebitPerShare = maximumDebit
    };
}
