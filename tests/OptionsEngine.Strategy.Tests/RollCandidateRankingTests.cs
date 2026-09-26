using System.Collections.Immutable;
using OptionsEngine.Strategy.Defense;
using OptionsEngine.Strategy.EntryStrategy;

namespace OptionsEngine.Strategy.Tests;

public sealed class RollCandidateRankingTests
{
    [Theory]
    [InlineData("rqs")]
    [InlineData("projectedDrs")]
    [InlineData("delta")]
    [InlineData("strike")]
    [InlineData("economics")]
    [InlineData("expiration")]
    [InlineData("symbol")]
    public void RankingUsesEveryApprovedTieBreakInOrder(string key)
    {
        var left = Candidate("SAME");
        var right = Candidate("SAME");
        left = key switch
        {
            "rqs" => left with { Rqs = AvailableRqs(51) },
            "projectedDrs" => WithProjectedDrs(left, 19),
            "delta" => left with { Candidate = left.Candidate with { Delta = .19 } },
            "strike" => left with { Candidate = left.Candidate with { Strike = 111 } },
            "economics" => left with
            {
                Metrics = left.Metrics with { NetRollPerShare = 1.01m }
            },
            "expiration" => left with
            {
                Candidate = left.Candidate with { Expiration = new DateOnly(2026, 10, 19) }
            },
            "symbol" => left with { Candidate = left.Candidate with { OptionSymbol = "A" } },
            _ => throw new ArgumentOutOfRangeException(nameof(key))
        };
        if (key == "symbol")
            right = right with { Candidate = right.Candidate with { OptionSymbol = "B" } };

        var ranked = RollCandidateEvaluator.Rank([right, left]);

        Assert.Equal(1, ranked[1].Rank);
        Assert.Equal(2, ranked[0].Rank);
    }

    [Fact]
    public void OnlyRankableCandidatesReceiveSequentialRanks()
    {
        var rejected = Candidate("REJECTED") with
        {
            State = RollCandidateEvaluationState.Rejected,
            ReasonCodes = [RollReasonCode.DteOutsideRange]
        };
        var insufficient = Candidate("INSUFFICIENT") with
        {
            State = RollCandidateEvaluationState.InsufficientData,
            MissingInputs = [RollMissingInputCode.CandidateAsk]
        };
        var lower = Candidate("LOWER") with { Rqs = AvailableRqs(1) };
        var higher = Candidate("HIGHER") with { Rqs = AvailableRqs(2) };

        var ranked = RollCandidateEvaluator.Rank([rejected, lower, insufficient, higher]);

        Assert.Null(ranked[0].Rank);
        Assert.Equal(2, ranked[1].Rank);
        Assert.Null(ranked[2].Rank);
        Assert.Equal(1, ranked[3].Rank);
    }

    [Fact]
    public void RankableCandidateHasNoMinimumRqsThreshold()
    {
        var ranked = Assert.Single(RollCandidateEvaluator.Rank(
            [Candidate("ZERO") with { Rqs = AvailableRqs(0) }]));

        Assert.Equal(RollCandidateEvaluationState.Rankable, ranked.State);
        Assert.Equal(1, ranked.Rank);
    }

    private static RollCandidateEvaluation WithProjectedDrs(RollCandidateEvaluation candidate,
        double score) => candidate with
    {
        Metrics = candidate.Metrics with
        {
            ProjectedDrsResult = AvailableDrs(score),
            DrsReduction = 80 - score
        }
    };

    private static RollCandidateEvaluation Candidate(string symbol)
    {
        var observation = new DefenseOptionObservation(symbol, "MSFT",
            new DateTimeOffset(2026, 9, 20, 16, 0, 0, TimeSpan.Zero),
            new DateOnly(2026, 10, 20), 110, OptionContractType.Call,
            1, 1.05m, null, 500, .30, .20, .02, -.01, .10, 105, "Test");
        var metrics = new RollCandidateDerivedMetrics(30, 20, ReplacementDteWindow.Preferred,
            false, 10, .10, .20, .05, 1, 2, 1, 1, 100, 0,
            AvailableDrs(20), 60);
        return new RollCandidateEvaluation(observation, RollCandidateEvaluationState.Rankable,
            metrics, [], [], AvailableRqs(50), null, []);
    }

    private static RqsResult AvailableRqs(double score) => new(EvaluationValueStatus.Available,
        score, ImmutableArray<RqsComponentResult>.Empty, [], "Test");

    private static DrsResult AvailableDrs(double score) => new(EvaluationValueStatus.Available,
        score, null, [], [], "Test");
}
