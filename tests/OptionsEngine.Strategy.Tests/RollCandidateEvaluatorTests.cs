using OptionsEngine.Domain.Accounts;
using OptionsEngine.Strategy.Defense;
using OptionsEngine.Strategy.EntryStrategy;
using OptionsEngine.Strategy.Indicators;

namespace OptionsEngine.Strategy.Tests;

public sealed class RollCandidateEvaluatorTests
{
    private static readonly DateTimeOffset Cutoff =
        new(2026, 9, 20, 16, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly EvaluationDate = new(2026, 9, 20);
    private readonly RollCandidateEvaluator evaluator = new();

    [Theory]
    [InlineData(20, RollCandidateEvaluationState.Rejected, null)]
    [InlineData(21, RollCandidateEvaluationState.EligibleForRqs, ReplacementDteWindow.Preferred)]
    [InlineData(45, RollCandidateEvaluationState.EligibleForRqs, ReplacementDteWindow.Preferred)]
    [InlineData(46, RollCandidateEvaluationState.EligibleForRqs, ReplacementDteWindow.Extended)]
    [InlineData(60, RollCandidateEvaluationState.EligibleForRqs, ReplacementDteWindow.Extended)]
    [InlineData(61, RollCandidateEvaluationState.Rejected, null)]
    public void ReplacementDteUsesInclusiveRangeAndRecordsWindow(int dte,
        RollCandidateEvaluationState expectedState, ReplacementDteWindow? expectedWindow)
    {
        var result = Evaluate(Candidate(dte: dte), Roll(maximumDebit: 2m));

        Assert.Equal(expectedState, result.State);
        Assert.Equal(dte, result.Metrics.NewDte);
        Assert.Equal(expectedWindow, result.Metrics.DteWindow);
        Assert.Equal(expectedState == RollCandidateEvaluationState.Rejected,
            result.ReasonCodes.Contains(RollReasonCode.DteOutsideRange));
    }

    [Theory]
    [InlineData(9)]
    [InlineData(10)]
    public void EarlierOrSameExpirationRejects(int dte)
    {
        var result = Evaluate(Candidate(dte: dte), Roll(maximumDebit: 2m));

        Assert.Equal(RollCandidateEvaluationState.Rejected, result.State);
        Assert.Contains(RollReasonCode.ExpirationNotImproved, result.ReasonCodes);
    }

    [Fact]
    public void LaterExpirationPassesExpirationGate()
    {
        var result = Evaluate(Candidate(dte: 21), Roll(maximumDebit: 2m));

        Assert.DoesNotContain(RollReasonCode.ExpirationNotImproved, result.ReasonCodes);
    }

    [Theory]
    [InlineData(99, RollReasonCode.StrikeNotImproved)]
    [InlineData(100, RollReasonCode.StrikeNotImproved)]
    [InlineData(101, RollReasonCode.StrikeNotStrictlyOtm)]
    [InlineData(105, RollReasonCode.StrikeNotStrictlyOtm)]
    public void StrikeEqualityLowerAndNonOtmCandidatesReject(decimal strike, RollReasonCode reason)
    {
        var result = Evaluate(Candidate(strike: strike), Roll(maximumDebit: 2m));

        Assert.Equal(RollCandidateEvaluationState.Rejected, result.State);
        Assert.Contains(reason, result.ReasonCodes);
    }

    [Fact]
    public void StrictlyHigherAndOtmStrikePassesBothStrikeGates()
    {
        var result = Evaluate(Candidate(strike: 106m), Roll(maximumDebit: 2m));

        Assert.DoesNotContain(RollReasonCode.StrikeNotImproved, result.ReasonCodes);
        Assert.DoesNotContain(RollReasonCode.StrikeNotStrictlyOtm, result.ReasonCodes);
    }

    [Fact]
    public void MissingUnderlyingMakesEligibilityInsufficient()
    {
        var result = Evaluate(Candidate(), currentUnderlyingPrice: null);

        Assert.Equal(RollCandidateEvaluationState.InsufficientData, result.State);
        Assert.Contains(RollMissingInputCode.UnderlyingPrice, result.MissingInputs);
        Assert.DoesNotContain(RollReasonCode.StrikeNotStrictlyOtm, result.ReasonCodes);
    }

    [Theory]
    [InlineData(.50)]
    [InlineData(.60)]
    public void SameOrHigherCandidateDeltaRejects(double delta)
    {
        var result = Evaluate(Candidate(delta: delta), Roll(maximumDebit: 2m));

        Assert.Equal(RollCandidateEvaluationState.Rejected, result.State);
        Assert.Contains(RollReasonCode.DeltaNotReduced, result.ReasonCodes);
    }

    [Theory]
    [InlineData(.25, false)]
    [InlineData(.250001, true)]
    public void NormalMaximumDeltaIsInclusive(double delta, bool rejected)
    {
        var result = Evaluate(Candidate(delta: delta), Roll(maximumDebit: 2m));

        Assert.Equal(rejected, result.ReasonCodes.Contains(RollReasonCode.DeltaExceedsMaximum));
    }

    [Theory]
    [InlineData(.20, false)]
    [InlineData(.200001, true)]
    public void HighTaxMaximumDeltaIsInclusive(double delta, bool rejected)
    {
        var result = Evaluate(Candidate(delta: delta), Roll(maximumDebit: 2m),
            taxSensitivity: TaxSensitivity.High);

        Assert.Equal(rejected, result.ReasonCodes.Contains(RollReasonCode.DeltaExceedsMaximum));
    }

    [Fact]
    public void ZeroCandidateDeltaPassesBecauseThereIsNoMinimumHardDelta()
    {
        var result = Evaluate(Candidate(delta: 0), Roll(maximumDebit: 2m));

        Assert.Equal(RollCandidateEvaluationState.EligibleForRqs, result.State);
        Assert.DoesNotContain(RollReasonCode.DeltaExceedsMaximum, result.ReasonCodes);
    }

    [Theory]
    [InlineData(.119999, false)]
    [InlineData(.12, true)]
    [InlineData(.18, true)]
    [InlineData(.180001, false)]
    public void PreferredDeltaWindowIsDerivedContextOnly(double delta, bool preferred)
    {
        var result = Evaluate(Candidate(delta: delta), Roll(maximumDebit: 2m));

        Assert.Equal(preferred, result.Metrics.PreferredDeltaWindow);
        Assert.DoesNotContain(RollReasonCode.DeltaExceedsMaximum, result.ReasonCodes);
    }

    [Theory]
    [MemberData(nameof(InvalidCandidateDeltas))]
    public void InvalidCandidateDeltaIsInsufficientAndNeverNormalized(double? delta)
    {
        var result = Evaluate(Candidate(delta: delta), Roll(maximumDebit: 2m));

        Assert.Equal(RollCandidateEvaluationState.InsufficientData, result.State);
        Assert.Contains(RollMissingInputCode.CandidateDelta, result.MissingInputs);
        Assert.DoesNotContain(RollReasonCode.DeltaExceedsMaximum, result.ReasonCodes);
    }

    [Theory]
    [MemberData(nameof(InvalidCandidateDeltas))]
    public void InvalidCurrentDeltaMakesReductionGateInsufficient(double? currentDelta)
    {
        var result = Evaluate(Candidate(), currentDelta: currentDelta);

        Assert.Equal(RollCandidateEvaluationState.InsufficientData, result.State);
        Assert.Contains(RollMissingInputCode.ExistingDelta, result.MissingInputs);
    }

    [Fact]
    public void PutIsRetainedAndExplicitlyRejected()
    {
        var result = Evaluate(Candidate() with { OptionType = OptionContractType.Put });

        Assert.Equal(RollCandidateEvaluationState.Rejected, result.State);
        Assert.Contains(RollReasonCode.OptionTypeNotCall, result.ReasonCodes);
    }

    [Fact]
    public void AskEqualToBidRejectsLiquidity()
    {
        var result = Evaluate(Candidate(bid: 1m, ask: 1m));

        Assert.Contains(RollReasonCode.InsufficientLiquidity, result.ReasonCodes);
    }

    [Theory]
    [InlineData(100, false)]
    [InlineData(99, true)]
    public void OpenInterestMinimumIsInclusive(long openInterest, bool rejected)
    {
        var result = Evaluate(Candidate(openInterest: openInterest));

        Assert.Equal(rejected, result.ReasonCodes.Contains(RollReasonCode.InsufficientLiquidity));
    }

    [Theory]
    [InlineData(.9, 1.1, false)]
    [InlineData(.8999, 1.1001, true)]
    public void LiquiditySpreadMaximumIsInclusive(decimal bid, decimal ask, bool rejected)
    {
        var result = Evaluate(Candidate(bid: bid, ask: ask), Roll(maximumDebit: 2m));

        Assert.Equal(rejected, result.ReasonCodes.Contains(RollReasonCode.InsufficientLiquidity));
        Assert.Equal((double)((ask - bid) / ((ask + bid) / 2m)),
            result.Metrics.BidAskSpreadPercent!.Value, 10);
    }

    [Theory]
    [InlineData("bid")]
    [InlineData("ask")]
    [InlineData("oi")]
    public void MissingLiquidityInputProducesInsufficientData(string field)
    {
        var candidate = field switch
        {
            "bid" => Candidate(bid: null),
            "ask" => Candidate(ask: null),
            _ => Candidate(openInterest: null)
        };
        var result = Evaluate(candidate, Roll(maximumDebit: 2m));

        Assert.Equal(RollCandidateEvaluationState.InsufficientData, result.State);
        Assert.Contains(field switch
        {
            "bid" => RollMissingInputCode.CandidateBid,
            "ask" => RollMissingInputCode.CandidateAsk,
            _ => RollMissingInputCode.CandidateOpenInterest
        }, result.MissingInputs);
    }

    [Fact]
    public void NonDefaultSharedPhaseFourLiquidityConfigurationIsHonored()
    {
        var configuration = CustomLiquidityRoll();
        var belowOi = Evaluate(Candidate(openInterest: 249), configuration);
        var exact = Evaluate(Candidate(bid: .925m, ask: 1.075m, openInterest: 250), configuration);
        var aboveSpread = Evaluate(Candidate(bid: .9249m, ask: 1.0751m, openInterest: 250), configuration);

        Assert.Contains(RollReasonCode.InsufficientLiquidity, belowOi.ReasonCodes);
        Assert.DoesNotContain(RollReasonCode.InsufficientLiquidity, exact.ReasonCodes);
        Assert.Contains(RollReasonCode.InsufficientLiquidity, aboveSpread.ReasonCodes);
    }

    [Theory]
    [InlineData(9, false)]
    [InlineData(10, false)]
    [InlineData(11, true)]
    [InlineData(30, true)]
    [InlineData(31, false)]
    public void StockRejectsOnlyNewlyIntroducedEarningsCrossing(int earningsDte, bool rejected)
    {
        var result = Evaluate(Candidate(dte: 30), earnings: new EarningsContext(
            AvailabilityStatus.Available, EvaluationDate.AddDays(earningsDte)));

        Assert.Equal(rejected, result.ReasonCodes.Contains(RollReasonCode.EarningsCrossing));
    }

    [Fact]
    public void MissingStockEarningsMakesCandidateInsufficient()
    {
        var result = Evaluate(Candidate(), earnings: new EarningsContext(AvailabilityStatus.Unavailable, null));

        Assert.Equal(RollCandidateEvaluationState.InsufficientData, result.State);
        Assert.Contains(RollMissingInputCode.EarningsDate, result.MissingInputs);
    }

    [Fact]
    public void EtfEarningsNotApplicableDoesNotBlockCandidate()
    {
        var result = Evaluate(Candidate(), assetType: AssetType.ExchangeTradedFund,
            earnings: new EarningsContext(AvailabilityStatus.NotApplicable, null));

        Assert.Equal(RollCandidateEvaluationState.EligibleForRqs, result.State);
        Assert.DoesNotContain(RollMissingInputCode.EarningsDate, result.MissingInputs);
    }

    [Theory]
    [InlineData(2.50, .50, 150)]
    [InlineData(2.00, 0, 0)]
    [InlineData(1.50, -.50, -150)]
    public void RollEconomicsUseExistingAskCandidateBidAndExistingContracts(decimal candidateBid,
        decimal expectedPerShare, decimal expectedTotal)
    {
        var result = Evaluate(Candidate(bid: candidateBid, ask: candidateBid + .10m),
            Roll(maximumDebit: 1m), contracts: 3);

        Assert.Equal(3, result.Metrics.ReplacementContracts);
        Assert.Equal(2m, result.Metrics.ExistingBtcPerShare);
        Assert.Equal(candidateBid, result.Metrics.ReplacementStoPerShare);
        Assert.Equal(expectedPerShare, result.Metrics.NetRollPerShare);
        Assert.Equal(expectedTotal, result.Metrics.NetRollTotal);
        Assert.Equal(Math.Max(-expectedPerShare, 0), result.Metrics.RollDebitPerShare);
    }

    [Theory]
    [InlineData(1.75, .25, false)]
    [InlineData(1.7499, .25, true)]
    [InlineData(1.99, 0, true)]
    [InlineData(2.00, 0, false)]
    [InlineData(2.01, 0, false)]
    public void MaximumDebitUsesInclusiveAbsoluteLimit(decimal candidateBid, decimal maximumDebit,
        bool rejected)
    {
        var result = Evaluate(Candidate(bid: candidateBid, ask: candidateBid + .10m),
            Roll(maximumDebit));

        Assert.Equal(rejected, result.ReasonCodes.Contains(RollReasonCode.DebitExceedsMaximum));
    }

    [Fact]
    public void ExistingAskAndCandidateBidNeverUseFallbackPrices()
    {
        var missingExistingAsk = Evaluate(Candidate(), currentAsk: null);
        var missingCandidateBid = Evaluate(Candidate(bid: null) with { Last = 9m, Ask = 9.1m },
            Roll(maximumDebit: 2m));

        Assert.Contains(RollMissingInputCode.ExistingAsk, missingExistingAsk.MissingInputs);
        Assert.Null(missingExistingAsk.Metrics.NetRollPerShare);
        Assert.Contains(RollMissingInputCode.CandidateBid, missingCandidateBid.MissingInputs);
        Assert.Null(missingCandidateBid.Metrics.ReplacementStoPerShare);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-.01)]
    public void NonPositiveExistingAskMakesEconomicsInsufficient(decimal ask)
    {
        var result = Evaluate(Candidate(), currentAsk: ask);

        Assert.Equal(RollCandidateEvaluationState.InsufficientData, result.State);
        Assert.Contains(RollMissingInputCode.ExistingAsk, result.MissingInputs);
        Assert.Null(result.Metrics.NetRollPerShare);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-.01)]
    public void NonPositiveCandidateBidLeavesEconomicsUnavailable(decimal bid)
    {
        var result = Evaluate(Candidate(bid: bid, ask: 1.10m), Roll(maximumDebit: 2m));

        Assert.Contains(RollMissingInputCode.CandidateBid, result.MissingInputs);
        Assert.Null(result.Metrics.ReplacementStoPerShare);
        Assert.Null(result.Metrics.NetRollPerShare);
    }

    [Fact]
    public void ProjectedDrsReusesPhaseSixBAndStartsPremiumMultipleAtOne()
    {
        var result = Evaluate(Candidate());
        var premium = Assert.Single(result.Metrics.ProjectedDrsResult.Components,
            component => component.Code == DrsComponentCode.PremiumExpansion);

        Assert.Equal(1, premium.ObservedValue);
        Assert.Equal(6, premium.Score);
        Assert.Equal(21, result.Metrics.ProjectedDrs);
        Assert.Equal(RollCandidateEvaluationState.EligibleForRqs, result.State);
    }

    [Theory]
    [InlineData(21, RollReasonCode.ProjectedDrsNotImproved)]
    [InlineData(20, RollReasonCode.ProjectedDrsNotImproved)]
    public void SameOrHigherProjectedDrsRejects(double currentDrs, RollReasonCode reason)
    {
        var result = Evaluate(Candidate(), currentDrs: AvailableDrs(currentDrs));

        Assert.Equal(RollCandidateEvaluationState.Rejected, result.State);
        Assert.Contains(reason, result.ReasonCodes);
    }

    [Fact]
    public void LowerProjectedDrsAtExactMaximumRejects()
    {
        var candidate = Candidate(dte: 21, strike: 107.5m, delta: .25);
        var result = Evaluate(candidate, Roll(maximumDebit: 2m), currentDrs: AvailableDrs(80));

        Assert.Equal(40, result.Metrics.ProjectedDrs);
        Assert.Contains(RollReasonCode.ProjectedDrsExceedsMaximum, result.ReasonCodes);
    }

    [Fact]
    public void LowerProjectedDrsBelowMaximumPassesPhaseSixCHardGates()
    {
        var result = Evaluate(Candidate(), currentDrs: AvailableDrs(80));

        Assert.Equal(21, result.Metrics.ProjectedDrs);
        Assert.Equal(59, result.Metrics.DrsReduction);
        Assert.Equal(RollCandidateEvaluationState.EligibleForRqs, result.State);
        Assert.Null(result.Rqs);
        Assert.Null(result.Rank);
    }

    [Fact]
    public void EligibleCandidatePreservesPhaseSixCDerivedMetrics()
    {
        var result = Evaluate(Candidate(), currentDrs: AvailableDrs(80));

        Assert.Equal(20, result.Metrics.AdditionalDte);
        Assert.Equal(10m, result.Metrics.StrikeImprovement);
        Assert.Equal(.10, result.Metrics.StrikeImprovementRatio, 10);
        Assert.Equal(.30, result.Metrics.DeltaReduction!.Value, 10);
        Assert.Equal(ReplacementDteWindow.Preferred, result.Metrics.DteWindow);
    }

    [Fact]
    public void MissingCurrentDrsMakesCandidateInsufficient()
    {
        var result = Evaluate(Candidate(), currentDrs: UnavailableDrs());

        Assert.Equal(RollCandidateEvaluationState.InsufficientData, result.State);
        Assert.Contains(RollMissingInputCode.CurrentDrs, result.MissingInputs);
    }

    [Fact]
    public void MissingProjectedDrsInputMakesCandidateInsufficient()
    {
        var result = Evaluate(Candidate(delta: null), Roll(maximumDebit: 2m));

        Assert.Equal(RollCandidateEvaluationState.InsufficientData, result.State);
        Assert.Contains(RollMissingInputCode.ProjectedDrs, result.MissingInputs);
    }

    [Fact]
    public void KnownFailureWinsWhileUnrelatedMissingInputsArePreserved()
    {
        var result = Evaluate(Candidate(dte: 20, ask: null), Roll(maximumDebit: 2m));

        Assert.Equal(RollCandidateEvaluationState.Rejected, result.State);
        Assert.Contains(RollReasonCode.DteOutsideRange, result.ReasonCodes);
        Assert.Contains(RollReasonCode.InsufficientData, result.ReasonCodes);
        Assert.Contains(RollMissingInputCode.CandidateAsk, result.MissingInputs);
    }

    [Fact]
    public void FutureSelectedChainObservationIsRejectedAtInputBoundary()
    {
        var candidate = Candidate() with { ObservationTimestampUtc = Cutoff.AddTicks(1) };
        var input = Input(candidate);

        Assert.Throws<ArgumentException>(() => evaluator.Evaluate(input));
    }

    [Fact]
    public void CandidateEvaluationRequiresActivatedRollEngine()
    {
        var input = Input(Candidate());
        input = input with { CurrentDefense = input.CurrentDefense with { RollEngineRequired = false } };

        Assert.Throws<ArgumentException>(() => evaluator.Evaluate(input));
    }

    [Fact]
    public void ValidPerExpirationSnapshotIsRetained()
    {
        var input = Input(Candidate());

        var result = evaluator.Evaluate(input);

        Assert.Single(result.SelectedChainSnapshots);
        Assert.Equal(input.SelectedChainSnapshots, result.SelectedChainSnapshots);
        Assert.Single(result.Candidates);
    }

    [Fact]
    public void EmptySelectedChainRemainsValidAndProducesNoCandidates()
    {
        var candidate = Candidate();
        var input = Input(candidate) with
        {
            SelectedChainSnapshots =
            [new SelectedRollChainSnapshot("MSFT", candidate.Expiration,
                candidate.ObservationTimestampUtc, candidate.Provider, [])]
        };

        var result = evaluator.Evaluate(input);

        Assert.Empty(result.Candidates);
    }

    public static TheoryData<double?> InvalidCandidateDeltas => new()
    {
        null, -.01, 1.000001, double.NaN, double.PositiveInfinity, double.NegativeInfinity
    };

    private RollCandidateEvaluation Evaluate(DefenseOptionObservation candidate,
        RollConfiguration? configuration = null, TaxSensitivity taxSensitivity = TaxSensitivity.Moderate,
        AssetType assetType = AssetType.Stock, EarningsContext? earnings = null,
        decimal? currentAsk = 2m, double? currentDelta = .50, decimal? currentUnderlyingPrice = 105m,
        int contracts = 1, DrsResult? currentDrs = null)
    {
        var input = Input(candidate, configuration, taxSensitivity, assetType, earnings,
            currentAsk, currentDelta, currentUnderlyingPrice, contracts, currentDrs);
        return Assert.Single(evaluator.Evaluate(input).Candidates);
    }

    private static RollCandidateEvaluationInput Input(DefenseOptionObservation candidate,
        RollConfiguration? configuration = null, TaxSensitivity taxSensitivity = TaxSensitivity.Moderate,
        AssetType assetType = AssetType.Stock, EarningsContext? earnings = null,
        decimal? currentAsk = 2m, double? currentDelta = .50, decimal? currentUnderlyingPrice = 105m,
        int contracts = 1, DrsResult? currentDrs = null)
    {
        var roll = configuration ?? Roll(maximumDebit: 1m);
        var defenseConfiguration = Defense();
        var version = new ConfigurationVersion(1);
        var holdingId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var existingExpiration = EvaluationDate.AddDays(10);
        var position = new OpenShortCallPositionSnapshot(42, holdingId, "CURRENT", contracts,
            100m, existingExpiration, 1m, null);
        var holding = new DefenseHoldingContext(holdingId, "MSFT", assetType,
            AssignmentSensitivity.Level3, taxSensitivity);
        var current = Observation("CURRENT", existingExpiration, 100m, OptionContractType.Call,
            1.9m, currentAsk, currentDelta, currentUnderlyingPrice, Cutoff.AddMinutes(-1), 500);
        var previous = Observation("CURRENT", existingExpiration, 100m, OptionContractType.Call,
            1.9m, currentAsk, .40, currentUnderlyingPrice, Cutoff.AddDays(-1), 500);
        var earningsContext = earnings ?? new EarningsContext(AvailabilityStatus.Available,
            candidate.Expiration.AddDays(1));
        var defenseInput = new DefenseEvaluationInput(position, holding, current, previous, null,
            earningsContext, Cutoff, defenseConfiguration, roll, version,
            new DefenseStrategyVersion("6.0.0"), new RollStrategyVersion("6.0.0"));
        var currentDefense = new DefenseEvaluator().Evaluate(defenseInput);
        if (currentDrs is not null) currentDefense = currentDefense with { Drs = currentDrs };
        var chain = new SelectedRollChainSnapshot(candidate.UnderlyingSymbol, candidate.Expiration,
            candidate.ObservationTimestampUtc, candidate.Provider, [candidate]);
        return new RollCandidateEvaluationInput(position, holding, current, currentDefense, earningsContext,
            Cutoff, [chain], defenseConfiguration.Drs, roll);
    }

    private static DefenseOptionObservation Candidate(int dte = 30, decimal strike = 110m,
        double? delta = .20, decimal? bid = 1m, decimal? ask = 1.10m, long? openInterest = 500) =>
        Observation("REPLACEMENT", EvaluationDate.AddDays(dte), strike, OptionContractType.Call,
            bid, ask, delta, 999m, Cutoff.AddMinutes(-1), openInterest);

    private static DefenseOptionObservation Observation(string optionSymbol, DateOnly expiration,
        decimal strike, OptionContractType optionType, decimal? bid, decimal? ask, double? delta,
        decimal? underlyingPrice, DateTimeOffset timestamp, long? openInterest) =>
        new(optionSymbol, "MSFT", timestamp, expiration, strike, optionType, bid, ask, null,
            openInterest, .30, delta, .02, -.01, .10, underlyingPrice, "Test");

    private static DefenseConfiguration Defense() => new() { Version = new ConfigurationVersion(1) };

    private static RollConfiguration Roll(decimal maximumDebit) => new()
    {
        Version = new ConfigurationVersion(1),
        MaximumRollDebitPerShare = maximumDebit
    };

    private static RollConfiguration CustomLiquidityRoll()
    {
        var scoring = new ContractLiquidityScoringConfiguration
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
        };
        return Roll(maximumDebit: 2m) with
        {
            LiquidityEligibility = new ContractLiquidityEligibilityConfiguration(250, .15),
            LiquidityScoring = scoring
        };
    }

    private static DrsResult AvailableDrs(double score) => new(EvaluationValueStatus.Available, score,
        DefenseEvaluator.Classify(score, Defense().Drs), [], [], "Test");

    private static DrsResult UnavailableDrs() => new(EvaluationValueStatus.InsufficientData, null,
        null, [], [DefenseMissingInputCode.CurrentDelta], "Test");
}
