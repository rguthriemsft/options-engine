using System.Collections.Immutable;
using OptionsEngine.Strategy.Defense;
using OptionsEngine.Strategy.EntryStrategy;
using OptionsEngine.Strategy.Indicators;

namespace OptionsEngine.Strategy.Tests;

public sealed class DefenseDispositionEvaluatorTests
{
    private readonly DefenseDispositionEvaluator evaluator = new();

    [Theory]
    [InlineData(ProfitTakingSignal.StrongCloseCandidate, DefenseDisposition.ProfitClose)]
    [InlineData(ProfitTakingSignal.CloseCandidate, DefenseDisposition.ProfitClose)]
    [InlineData(ProfitTakingSignal.Monitor, DefenseDisposition.Monitor)]
    [InlineData(ProfitTakingSignal.None, DefenseDisposition.NoAction)]
    public void NoDefensiveActivationUsesProfitTaking(ProfitTakingSignal signal,
        DefenseDisposition expected)
    {
        var result = Evaluate(Defense(false, signal, AvailableDrs(10), HardDefenseStatus.Clear));

        Assert.Equal(expected, result.Disposition);
    }

    [Fact]
    public void MissingProfitTakingWithoutDefenseProducesReview()
    {
        var defense = Defense(false, ProfitTakingSignal.None, AvailableDrs(10), HardDefenseStatus.Clear) with
        {
            ProfitTaking = new ProfitTakingResult(EvaluationValueStatus.InsufficientData,
                ProfitTakingSignal.None, null, null, null, null,
                [DefenseMissingInputCode.OpeningPremiumPerShare], "Test")
        };

        var result = Evaluate(defense);

        Assert.Equal(DefenseDisposition.DefenseReview, result.Disposition);
        Assert.Contains(DefenseMissingInputCode.OpeningPremiumPerShare, result.MissingInputs);
    }

    [Fact]
    public void ClearHardDefenseAndUnavailableDrsDoesNotBecomeNoAction()
    {
        var result = Evaluate(Defense(false, ProfitTakingSignal.None,
            UnavailableDrs(), HardDefenseStatus.Clear));

        Assert.Equal(DefenseDisposition.DefenseReview, result.Disposition);
        Assert.Contains(DefenseMissingInputCode.CurrentDelta, result.MissingInputs);
    }

    [Theory]
    [InlineData(55, DefenseDisposition.Roll)]
    [InlineData(54.999999, DefenseDisposition.CloseWait)]
    public void RankableCandidateUsesInclusiveCurrentCcosThreshold(double ccos,
        DefenseDisposition expected)
    {
        var result = Evaluate(Defense(true, ProfitTakingSignal.None,
            AvailableDrs(60), HardDefenseStatus.Clear), Candidates(RollCandidateEvaluationState.Rankable), ccos);

        Assert.Equal(expected, result.Disposition);
    }

    [Theory]
    [MemberData(nameof(InvalidCurrentCcosValues))]
    public void MissingOrMalformedRequiredCurrentCcosProducesReview(double? ccos)
    {
        var result = Evaluate(Defense(true, ProfitTakingSignal.None,
            AvailableDrs(60), HardDefenseStatus.Clear), Candidates(RollCandidateEvaluationState.Rankable), ccos);

        Assert.Equal(DefenseDisposition.DefenseReview, result.Disposition);
        Assert.Contains(DefenseMissingInputCode.CurrentCcos, result.MissingInputs);
    }

    [Fact]
    public void DefensiveActivationTakesPrecedenceOverProfitTaking()
    {
        var result = Evaluate(Defense(true, ProfitTakingSignal.StrongCloseCandidate,
            AvailableDrs(60), HardDefenseStatus.Clear), Candidates(RollCandidateEvaluationState.Rankable), 55);

        Assert.Equal(DefenseDisposition.Roll, result.Disposition);
        Assert.NotEqual(DefenseDisposition.ProfitClose, result.Disposition);
    }

    [Fact]
    public void NoRankableCandidateAndAnyInsufficientCandidateProducesReview()
    {
        var result = Evaluate(Defense(true, ProfitTakingSignal.None,
            AvailableDrs(60), HardDefenseStatus.Clear), Candidates(
            RollCandidateEvaluationState.Rejected, RollCandidateEvaluationState.InsufficientData), 80);

        Assert.Equal(DefenseDisposition.DefenseReview, result.Disposition);
    }

    [Fact]
    public void AllRejectedWithHardTriggerProducesReview()
    {
        var result = Evaluate(Defense(true, ProfitTakingSignal.None,
            AvailableDrs(30), HardDefenseStatus.Triggered),
            Candidates(RollCandidateEvaluationState.Rejected), 80);

        Assert.Equal(DefenseDisposition.DefenseReview, result.Disposition);
        Assert.Contains(DefenseReasonCode.HardTriggerActivation, result.ReasonCodes);
    }

    [Fact]
    public void AllRejectedWithDrsOnlyActivationProducesCloseWait()
    {
        var result = Evaluate(Defense(true, ProfitTakingSignal.None,
            AvailableDrs(50), HardDefenseStatus.Clear),
            Candidates(RollCandidateEvaluationState.Rejected), 80);

        Assert.Equal(DefenseDisposition.CloseWait, result.Disposition);
        Assert.Contains(DefenseReasonCode.DrsActivation, result.ReasonCodes);
    }

    [Fact]
    public void PartiallyEvaluatedWithActivatingDrsUsesNormalDefensiveLogic()
    {
        var result = Evaluate(Defense(true, ProfitTakingSignal.StrongCloseCandidate,
            AvailableDrs(50), HardDefenseStatus.PartiallyEvaluated),
            Candidates(RollCandidateEvaluationState.Rankable), 55);

        Assert.Equal(DefenseDisposition.Roll, result.Disposition);
    }

    [Fact]
    public void PartiallyEvaluatedBelowActivationProducesReview()
    {
        var result = Evaluate(Defense(false, ProfitTakingSignal.None,
            AvailableDrs(49), HardDefenseStatus.PartiallyEvaluated));

        Assert.Equal(DefenseDisposition.DefenseReview, result.Disposition);
        Assert.NotEqual(DefenseDisposition.NoAction, result.Disposition);
    }

    [Fact]
    public void PartiallyEvaluatedWithUnavailableDrsProducesReview()
    {
        var result = Evaluate(Defense(false, ProfitTakingSignal.None,
            UnavailableDrs(), HardDefenseStatus.PartiallyEvaluated));

        Assert.Equal(DefenseDisposition.DefenseReview, result.Disposition);
        Assert.NotEqual(DefenseDisposition.NoAction, result.Disposition);
    }

    public static TheoryData<double?> InvalidCurrentCcosValues => new()
    {
        null, -0.000001, 100.000001, double.NaN, double.PositiveInfinity, double.NegativeInfinity
    };

    private DefenseDispositionResult Evaluate(DefenseStrategyResult defense,
        RollCandidateStrategyResult? candidates = null, double? ccos = null) =>
        evaluator.Evaluate(new DefenseDispositionEvaluationInput(defense, candidates, ccos, Roll()));

    private static DefenseStrategyResult Defense(bool rollRequired, ProfitTakingSignal signal,
        DrsResult drs, HardDefenseStatus hardDefenseStatus) => new(
        new DateOnly(2026, 9, 20), 10,
        new ProfitTakingResult(EvaluationValueStatus.Available, signal, 100, 50, 50, .5, [], "Test"),
        drs, null, [], hardDefenseStatus, rollRequired);

    private static RollCandidateStrategyResult Candidates(params RollCandidateEvaluationState[] states)
    {
        var candidates = states.Select((state, index) => Candidate(state, index)).ToImmutableArray();
        var preferred = candidates.FirstOrDefault(candidate =>
            candidate.State == RollCandidateEvaluationState.Rankable);
        return new RollCandidateStrategyResult([], candidates, preferred?.Candidate.OptionSymbol,
            preferred?.Candidate.Strike, preferred?.Candidate.Expiration, preferred?.Rqs?.Score);
    }

    private static RollCandidateEvaluation Candidate(RollCandidateEvaluationState state, int index)
    {
        var observation = new DefenseOptionObservation($"C{index}", "MSFT",
            new DateTimeOffset(2026, 9, 20, 16, 0, 0, TimeSpan.Zero),
            new DateOnly(2026, 10, 20), 110, OptionContractType.Call,
            1, 1.05m, null, 500, .30, .20, .02, -.01, .10, 105, "Test");
        var projected = AvailableDrs(20);
        var metrics = new RollCandidateDerivedMetrics(30, 20, ReplacementDteWindow.Preferred,
            false, 10, .10, .20, .05, 1, 2, 1, 1, 100, 0, projected, 40);
        var rqs = state == RollCandidateEvaluationState.Rankable
            ? new RqsResult(EvaluationValueStatus.Available, 50, [], [], "Test")
            : null;
        return new RollCandidateEvaluation(observation, state, metrics,
            state == RollCandidateEvaluationState.Rejected ? [RollReasonCode.DteOutsideRange] : [],
            state == RollCandidateEvaluationState.InsufficientData ? [RollMissingInputCode.CandidateAsk] : [],
            rqs, state == RollCandidateEvaluationState.Rankable ? 1 : null, []);
    }

    private static DrsResult AvailableDrs(double score) => new(EvaluationValueStatus.Available,
        score, null, [], [], "Test");

    private static DrsResult UnavailableDrs() => new(EvaluationValueStatus.InsufficientData,
        null, null, [], [DefenseMissingInputCode.CurrentDelta], "Test");

    private static RollConfiguration Roll() => new()
    {
        Version = new ConfigurationVersion(1),
        MaximumRollDebitPerShare = 1
    };
}
