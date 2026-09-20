using System.Collections.Immutable;
using OptionsEngine.Domain.Accounts;
using OptionsEngine.Strategy.Defense;
using OptionsEngine.Strategy.EntryStrategy;
using OptionsEngine.Strategy.Indicators;

namespace OptionsEngine.Strategy.Tests;

public sealed class DefenseEvaluatorTests
{
    private readonly DefenseEvaluator evaluator = new();

    public static TheoryData<decimal, ProfitTakingSignal> ProfitTakingBoundaries => new()
    {
        { .4999m, ProfitTakingSignal.None }, { .50m, ProfitTakingSignal.Monitor },
        { .5001m, ProfitTakingSignal.Monitor }, { .6999m, ProfitTakingSignal.Monitor },
        { .70m, ProfitTakingSignal.CloseCandidate }, { .7001m, ProfitTakingSignal.CloseCandidate },
        { .7999m, ProfitTakingSignal.CloseCandidate }, { .80m, ProfitTakingSignal.StrongCloseCandidate },
        { .8001m, ProfitTakingSignal.StrongCloseCandidate }
    };

    public static TheoryData<double, double> DeltaScoreBoundaries => new()
    {
        { .149999, 0 }, { .15, 4 }, { .150001, 4 },
        { .199999, 4 }, { .20, 9 }, { .200001, 9 },
        { .249999, 9 }, { .25, 16 }, { .250001, 16 },
        { .299999, 16 }, { .30, 24 }, { .300001, 24 },
        { .349999, 24 }, { .35, 31 }, { .350001, 31 },
        { .399999, 31 }, { .40, 36 }, { .400001, 36 },
        { .499999, 36 }, { .50, 36 }, { .500001, 40 }
    };

    public static TheoryData<double> InvalidDeltas => new()
    {
        -0.01, 1.000001, double.NaN, double.PositiveInfinity, double.NegativeInfinity
    };

    public static TheoryData<decimal, double> StrikeDistanceBoundaries => new()
    {
        { -.000001m, 25 }, { 0m, 25 }, { .000001m, 25 },
        { .009999m, 25 }, { .01m, 25 }, { .010001m, 20 },
        { .019999m, 20 }, { .02m, 20 }, { .020001m, 15 },
        { .029999m, 15 }, { .03m, 15 }, { .030001m, 10 },
        { .039999m, 10 }, { .04m, 10 }, { .040001m, 6 },
        { .059999m, 6 }, { .06m, 6 }, { .060001m, 3 },
        { .079999m, 3 }, { .08m, 3 }, { .080001m, 0 }
    };

    public static TheoryData<int, double?> DteBoundaries => new()
    {
        { -1, null }, { 0, 15 }, { 1, 15 }, { 3, 15 }, { 4, 12 }, { 6, 12 },
        { 7, 9 }, { 9, 9 }, { 10, 6 }, { 14, 6 }, { 15, 3 }, { 21, 3 }, { 22, 0 }
    };

    public static TheoryData<decimal, double> PremiumMultipleBoundaries => new()
    {
        { .499999m, 0 }, { .50m, 2 }, { .500001m, 2 },
        { .999999m, 2 }, { 1m, 6 }, { 1.000001m, 6 },
        { 1.249999m, 6 }, { 1.25m, 10 }, { 1.250001m, 10 },
        { 1.499999m, 10 }, { 1.50m, 14 }, { 1.500001m, 14 },
        { 1.999999m, 14 }, { 2m, 18 }, { 2.000001m, 18 },
        { 2.999999m, 18 }, { 3m, 20 }, { 3.000001m, 20 }
    };

    public static TheoryData<double, DrsClassification> ClassificationBoundaries => new()
    {
        { 19.999999, DrsClassification.Safe }, { 20, DrsClassification.Normal },
        { 20.000001, DrsClassification.Normal }, { 34.999999, DrsClassification.Normal },
        { 35, DrsClassification.Watch }, { 35.000001, DrsClassification.Watch },
        { 49.999999, DrsClassification.Watch }, { 50, DrsClassification.Defend },
        { 50.000001, DrsClassification.Defend }, { 64.999999, DrsClassification.Defend },
        { 65, DrsClassification.HighRisk }, { 65.000001, DrsClassification.HighRisk },
        { 79.999999, DrsClassification.HighRisk }, { 80, DrsClassification.Critical },
        { 80.000001, DrsClassification.Critical }
    };

    [Theory]
    [MemberData(nameof(ProfitTakingBoundaries))]
    public void ProfitTakingUsesExactInclusiveBands(decimal capturedRatio, ProfitTakingSignal expected)
    {
        var result = evaluator.Evaluate(Input(ask: 1m - capturedRatio)).ProfitTaking;

        Assert.Equal(EvaluationValueStatus.Available, result.Status);
        Assert.Equal(expected, result.Signal);
        Assert.Equal((double)capturedRatio, result.GrossPremiumCapturedRatio);
    }

    [Fact]
    public void ProfitTakingUsesAskAndGrossContractEconomics()
    {
        var input = Input(openingPremium: 2m, ask: .50m) with
        {
            Position = Position(2m, contracts: 3)
        };

        var result = evaluator.Evaluate(input).ProfitTaking;

        Assert.Equal(600m, result.GrossOpeningPremium);
        Assert.Equal(150m, result.EstimatedCurrentBtcCost);
        Assert.Equal(450m, result.GrossPremiumCaptured);
        Assert.Equal(.75, result.GrossPremiumCapturedRatio);
        Assert.Equal(ProfitTakingSignal.CloseCandidate, result.Signal);
    }

    [Fact]
    public void AskAboveOpeningPremiumProducesUnclampedNegativeCapture()
    {
        var result = evaluator.Evaluate(Input(openingPremium: 1m, ask: 1.25m)).ProfitTaking;

        Assert.Equal(EvaluationValueStatus.Available, result.Status);
        Assert.Equal(-.25, result.GrossPremiumCapturedRatio);
        Assert.Equal(ProfitTakingSignal.None, result.Signal);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, -1)]
    [InlineData(true, 0)]
    [InlineData(true, -1)]
    public void NonPositiveOrMissingProfitInputsAreInsufficient(bool invalidAsk, decimal value)
    {
        var input = invalidAsk ? Input(ask: value) : Input(openingPremium: value);
        if (value == -1)
            input = invalidAsk
                ? input with { CurrentOptionObservation = input.CurrentOptionObservation! with { Ask = null } }
                : input with { Position = input.Position with { OpeningPremiumPerShare = null } };

        var result = evaluator.Evaluate(input).ProfitTaking;

        Assert.Equal(EvaluationValueStatus.InsufficientData, result.Status);
        Assert.Null(result.GrossPremiumCapturedRatio);
        Assert.Contains(invalidAsk ? DefenseMissingInputCode.CurrentAsk :
            DefenseMissingInputCode.OpeningPremiumPerShare, result.MissingInputs);
    }

    [Fact]
    public void ProfitTakingDoesNotFallBackFromMissingAsk()
    {
        var input = Input() with
        {
            CurrentOptionObservation = Input().CurrentOptionObservation! with
            {
                Ask = null, Bid = .01m, Last = .01m
            }
        };

        Assert.Equal(EvaluationValueStatus.InsufficientData, evaluator.Evaluate(input).ProfitTaking.Status);
    }

    [Theory]
    [MemberData(nameof(DeltaScoreBoundaries))]
    public void DeltaComponentUsesApprovedBands(double delta, double expectedScore)
    {
        var component = Component(evaluator.Evaluate(Input(delta: delta)), DrsComponentCode.Delta);

        Assert.Equal(EvaluationValueStatus.Available, component.Status);
        Assert.Equal(delta, component.ObservedValue);
        Assert.Equal(expectedScore, component.Score);
        Assert.Equal(40, component.MaximumScore);
    }

    [Theory]
    [MemberData(nameof(InvalidDeltas))]
    public void InvalidDeltaIsUnavailableAndNeverNormalized(double delta)
    {
        var result = evaluator.Evaluate(Input(delta: delta));
        var component = Component(result, DrsComponentCode.Delta);

        Assert.Equal(EvaluationValueStatus.InsufficientData, component.Status);
        Assert.Null(component.Score);
        Assert.Equal(EvaluationValueStatus.InsufficientData, result.Drs.Status);
        Assert.Null(result.Drs.Score);
        Assert.Contains(DefenseMissingInputCode.CurrentDelta, result.Drs.MissingInputs);
        Assert.Equal(HardTriggerStatus.InsufficientData,
            Trigger(result, HardTriggerCode.HighDelta).Status);
    }

    [Theory]
    [MemberData(nameof(StrikeDistanceBoundaries))]
    public void StrikeProximityUsesApprovedBands(decimal distance, double expectedScore)
    {
        const decimal underlying = 100m;
        var strike = underlying * (1m + distance);
        var component = Component(evaluator.Evaluate(Input(strike: strike, underlyingPrice: underlying)),
            DrsComponentCode.StrikeProximity);

        Assert.Equal(EvaluationValueStatus.Available, component.Status);
        Assert.Equal((double)distance, component.ObservedValue!.Value, 10);
        Assert.Equal(expectedScore, component.Score);
    }

    [Fact]
    public void StrikeProximityRequiresPositiveUnderlyingAndStructuralStrike()
    {
        var zeroUnderlying = evaluator.Evaluate(Input(underlyingPrice: 0));
        var zeroStrike = Input() with { Position = Position(1m, strike: 0) };

        Assert.Equal(EvaluationValueStatus.InsufficientData,
            Component(zeroUnderlying, DrsComponentCode.StrikeProximity).Status);
        Assert.Throws<ArgumentOutOfRangeException>(() => evaluator.Evaluate(zeroStrike));
    }

    [Theory]
    [MemberData(nameof(DteBoundaries))]
    public void DteComponentUsesApprovedBandsAndRejectsNegativeDte(int dte, double? expectedScore)
    {
        var result = evaluator.Evaluate(Input(dte: dte));
        var component = Component(result, DrsComponentCode.Dte);

        if (expectedScore is null)
        {
            Assert.Equal(EvaluationValueStatus.InsufficientData, component.Status);
            Assert.Null(component.Score);
            Assert.Equal(EvaluationValueStatus.InsufficientData, result.Drs.Status);
            Assert.Equal(HardTriggerStatus.InsufficientData,
                Trigger(result, HardTriggerCode.LowDteWithDelta).Status);
        }
        else
        {
            Assert.Equal(EvaluationValueStatus.Available, component.Status);
            Assert.Equal(expectedScore, component.Score);
        }
    }

    [Fact]
    public void DteUsesNewYorkDateWhenUtcCalendarDateDiffers()
    {
        var timestamp = new DateTimeOffset(2026, 9, 20, 1, 0, 0, TimeSpan.Zero);
        var input = Input(timestamp: timestamp, dte: 1);

        var result = evaluator.Evaluate(input);

        Assert.Equal(new DateOnly(2026, 9, 19), result.DefenseEvaluationDate);
        Assert.Equal(1, result.Dte);
        Assert.Equal(15, Component(result, DrsComponentCode.Dte).Score);
    }

    [Theory]
    [MemberData(nameof(PremiumMultipleBoundaries))]
    public void PremiumExpansionUsesApprovedBands(decimal multiple, double expectedScore)
    {
        var component = Component(evaluator.Evaluate(Input(openingPremium: 1m, ask: multiple)),
            DrsComponentCode.PremiumExpansion);

        Assert.Equal(EvaluationValueStatus.Available, component.Status);
        Assert.Equal((double)multiple, component.ObservedValue);
        Assert.Equal(expectedScore, component.Score);
        Assert.Equal(20, component.MaximumScore);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PremiumExpansionRequiresOpeningPremiumAndAsk(bool missingOpeningPremium)
    {
        var input = missingOpeningPremium ? Input(openingPremium: null) : Input(ask: null);
        var component = Component(evaluator.Evaluate(input), DrsComponentCode.PremiumExpansion);

        Assert.Equal(EvaluationValueStatus.InsufficientData, component.Status);
        Assert.Null(component.Score);
    }

    [Theory]
    [MemberData(nameof(ClassificationBoundaries))]
    public void DrsClassificationUsesApprovedBoundaries(double score, DrsClassification expected)
    {
        Assert.Equal(expected, DefenseEvaluator.Classify(score, Defense().Drs));
    }

    [Fact]
    public void OneUnavailableDrsComponentMakesTotalUnavailableButPreservesAvailableComponents()
    {
        var result = evaluator.Evaluate(Input(delta: null));

        Assert.Equal(EvaluationValueStatus.InsufficientData, result.Drs.Status);
        Assert.Null(result.Drs.Score);
        Assert.Null(result.Drs.Classification);
        Assert.Equal(4, result.Drs.Components.Length);
        Assert.Equal(3, result.Drs.Components.Count(component =>
            component.Status == EvaluationValueStatus.Available));
        Assert.Equal(EvaluationValueStatus.InsufficientData,
            Component(result, DrsComponentCode.Delta).Status);
    }

    [Fact]
    public void CompleteDrsIsBoundedAndContainsExactlyFourComponents()
    {
        var result = evaluator.Evaluate(Input(delta: 1, strike: 100m, underlyingPrice: 101m,
            dte: 0, openingPremium: 1m, ask: 3m));

        Assert.Equal(EvaluationValueStatus.Available, result.Drs.Status);
        Assert.Equal(100, result.Drs.Score);
        Assert.Equal(DrsClassification.Critical, result.Drs.Classification);
        Assert.Equal(4, result.Drs.Components.Length);
        Assert.Equal([DrsComponentCode.Delta, DrsComponentCode.StrikeProximity,
            DrsComponentCode.Dte, DrsComponentCode.PremiumExpansion],
            result.Drs.Components.Select(component => component.Code));
    }

    [Fact]
    public void ExactHighDeltaThresholdTriggers()
    {
        Assert.Equal(HardTriggerStatus.Triggered,
            Trigger(evaluator.Evaluate(Input(delta: .40)), HardTriggerCode.HighDelta).Status);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(101)]
    public void ExactStrikeProximityBoundariesAndDeltaThresholdTrigger(decimal strike)
    {
        var result = evaluator.Evaluate(Input(delta: .30, strike: strike, underlyingPrice: 100m));

        Assert.Equal(HardTriggerStatus.Triggered,
            Trigger(result, HardTriggerCode.StrikeProximityWithDelta).Status);
    }

    [Fact]
    public void ItmTriggerUsesStrictInequality()
    {
        var atm = evaluator.Evaluate(Input(strike: 100m, underlyingPrice: 100m));
        var itm = evaluator.Evaluate(Input(strike: 100m, underlyingPrice: 100.01m));

        Assert.Equal(HardTriggerStatus.NotTriggered, Trigger(atm, HardTriggerCode.InTheMoney).Status);
        Assert.Equal(HardTriggerStatus.Triggered, Trigger(itm, HardTriggerCode.InTheMoney).Status);
        Assert.Equal(HardTriggerStatus.NotApplicable,
            Trigger(itm, HardTriggerCode.StrikeProximityWithDelta).Status);
    }

    [Fact]
    public void ExactLowDteAndDeltaThresholdsTrigger()
    {
        var result = evaluator.Evaluate(Input(dte: 3, delta: .25));

        Assert.Equal(HardTriggerStatus.Triggered,
            Trigger(result, HardTriggerCode.LowDteWithDelta).Status);
    }

    [Fact]
    public void ExactRapidDeltaThresholdTriggersAndDecreaseRemainsNegative()
    {
        var exact = evaluator.Evaluate(Input(delta: .35, previousDelta: .20));
        var below = evaluator.Evaluate(Input(delta: .349999, previousDelta: .20));
        var decrease = evaluator.Evaluate(Input(delta: .20, previousDelta: .40));

        Assert.Equal(.15, exact.DeltaVelocity!.Value, 10);
        Assert.Equal(HardTriggerStatus.Triggered,
            Trigger(exact, HardTriggerCode.RapidDeltaIncrease).Status);
        Assert.Equal(HardTriggerStatus.NotTriggered,
            Trigger(below, HardTriggerCode.RapidDeltaIncrease).Status);
        Assert.Equal(-.20, decrease.DeltaVelocity);
        Assert.Equal(HardTriggerStatus.NotTriggered,
            Trigger(decrease, HardTriggerCode.RapidDeltaIncrease).Status);
    }

    [Theory]
    [MemberData(nameof(InvalidDeltas))]
    public void InvalidPreviousDeltaMakesVelocityUnavailable(double previousDelta)
    {
        var result = evaluator.Evaluate(Input(delta: .20, previousDelta: previousDelta));

        Assert.Null(result.DeltaVelocity);
        var trigger = Trigger(result, HardTriggerCode.RapidDeltaIncrease);
        Assert.Equal(HardTriggerStatus.InsufficientData, trigger.Status);
        Assert.Contains(DefenseMissingInputCode.PreviousDelta, trigger.MissingInputs);
    }

    [Fact]
    public void EveryHardTriggerReportsRequiredMissingData()
    {
        var missingCurrentDelta = evaluator.Evaluate(Input(delta: null, strike: 100m,
            underlyingPrice: 100m, dte: 3));
        var missingUnderlying = evaluator.Evaluate(Input(underlyingPrice: null));
        var missingPrevious = evaluator.Evaluate(Input(includePreviousObservation: false));

        Assert.Equal(HardTriggerStatus.InsufficientData,
            Trigger(missingCurrentDelta, HardTriggerCode.HighDelta).Status);
        Assert.Equal(HardTriggerStatus.InsufficientData,
            Trigger(missingCurrentDelta, HardTriggerCode.StrikeProximityWithDelta).Status);
        Assert.Equal(HardTriggerStatus.InsufficientData,
            Trigger(missingCurrentDelta, HardTriggerCode.LowDteWithDelta).Status);
        Assert.Equal(HardTriggerStatus.NotTriggered,
            Trigger(missingCurrentDelta, HardTriggerCode.InTheMoney).Status);
        Assert.Equal(HardTriggerStatus.InsufficientData,
            Trigger(missingUnderlying, HardTriggerCode.StrikeProximityWithDelta).Status);
        Assert.Equal(HardTriggerStatus.InsufficientData,
            Trigger(missingUnderlying, HardTriggerCode.InTheMoney).Status);
        Assert.Equal(HardTriggerStatus.InsufficientData,
            Trigger(missingPrevious, HardTriggerCode.RapidDeltaIncrease).Status);
        Assert.Contains(DefenseMissingInputCode.PreviousDelta,
            Trigger(missingPrevious, HardTriggerCode.RapidDeltaIncrease).MissingInputs);
    }

    [Fact]
    public void TriggeredRuleWinsOverUnavailableRule()
    {
        var result = evaluator.Evaluate(Input(delta: .40, includePreviousObservation: false));

        Assert.Equal(HardTriggerStatus.Triggered, Trigger(result, HardTriggerCode.HighDelta).Status);
        Assert.Equal(HardTriggerStatus.InsufficientData,
            Trigger(result, HardTriggerCode.RapidDeltaIncrease).Status);
        Assert.Equal(HardDefenseStatus.Triggered, result.HardDefenseStatus);
    }

    [Fact]
    public void UnavailableRuleWithoutTriggerProducesPartiallyEvaluated()
    {
        var result = evaluator.Evaluate(Input(delta: .10, includePreviousObservation: false));

        Assert.DoesNotContain(result.HardTriggers, trigger => trigger.Status == HardTriggerStatus.Triggered);
        Assert.Equal(HardDefenseStatus.PartiallyEvaluated, result.HardDefenseStatus);
    }

    [Fact]
    public void AllKnownFalseRulesProduceClearHardDefenseStatus()
    {
        var result = evaluator.Evaluate(Input(delta: .10, previousDelta: .10, underlyingPrice: 80m, dte: 30));

        Assert.Equal(5, result.HardTriggers.Length);
        Assert.All(result.HardTriggers,
            trigger => Assert.Equal(HardTriggerStatus.NotTriggered, trigger.Status));
        Assert.Equal(HardDefenseStatus.Clear, result.HardDefenseStatus);
    }

    [Fact]
    public void NonPositiveContractCountFailsStructuralInputValidation()
    {
        var input = Input() with { Position = Position(1m, contracts: 0) };

        Assert.Throws<ArgumentOutOfRangeException>(() => evaluator.Evaluate(input));
    }

    [Fact]
    public void RollActivationUsesExactDrsThreshold()
    {
        var configuration = Roll();
        var below = AvailableDrs(49.999999);
        var exact = AvailableDrs(50);

        Assert.False(DefenseEvaluator.RequiresRollEngine(below, HardDefenseStatus.Clear, configuration));
        Assert.True(DefenseEvaluator.RequiresRollEngine(exact, HardDefenseStatus.Clear, configuration));
    }

    [Fact]
    public void HardTriggerActivatesRollWhenDrsIsUnavailable()
    {
        Assert.True(DefenseEvaluator.RequiresRollEngine(UnavailableDrs(), HardDefenseStatus.Triggered, Roll()));
        Assert.False(DefenseEvaluator.RequiresRollEngine(UnavailableDrs(), HardDefenseStatus.Clear, Roll()));
        Assert.False(DefenseEvaluator.RequiresRollEngine(UnavailableDrs(), HardDefenseStatus.PartiallyEvaluated, Roll()));
    }

    [Fact]
    public void EvaluatorActivatesRollAtDrsThresholdWithoutHardTrigger()
    {
        var result = evaluator.Evaluate(Input(delta: .10, previousDelta: .10, strike: 100m,
            underlyingPrice: 100m, dte: 4, openingPremium: 1m, ask: 2m));

        Assert.Equal(HardDefenseStatus.Clear, result.HardDefenseStatus);
        Assert.Equal(55, result.Drs.Score);
        Assert.True(result.RollEngineRequired);
    }

    [Fact]
    public void EvaluatorActivatesRollForHardTriggerWithUnavailableDrs()
    {
        var result = evaluator.Evaluate(Input(delta: .40, previousDelta: null,
            openingPremium: null));

        Assert.Equal(EvaluationValueStatus.InsufficientData, result.Drs.Status);
        Assert.Equal(HardDefenseStatus.Triggered, result.HardDefenseStatus);
        Assert.True(result.RollEngineRequired);
    }

    [Fact]
    public void ProfitTakingAloneDoesNotActivateRollEngine()
    {
        var result = evaluator.Evaluate(Input(openingPremium: 1m, ask: .20m, delta: null,
            previousDelta: .10, underlyingPrice: 80m, dte: 30));

        Assert.Equal(ProfitTakingSignal.StrongCloseCandidate, result.ProfitTaking.Signal);
        Assert.Equal(EvaluationValueStatus.Available, result.ProfitTaking.Status);
        Assert.Equal(EvaluationValueStatus.InsufficientData, result.Drs.Status);
        Assert.False(result.RollEngineRequired);
    }

    [Fact]
    public void CurrentCcosDoesNotAlterRollActivation()
    {
        var low = evaluator.Evaluate(Input(currentCcos: 0, delta: .10, previousDelta: .10,
            underlyingPrice: 80m, dte: 30));
        var high = evaluator.Evaluate(Input(currentCcos: 100, delta: .10, previousDelta: .10,
            underlyingPrice: 80m, dte: 30));

        Assert.Equal(low.RollEngineRequired, high.RollEngineRequired);
        Assert.False(high.RollEngineRequired);
    }

    private static ExplanationComponent Component(DefenseStrategyResult result, DrsComponentCode code) =>
        Assert.Single(result.Drs.Components, component => component.Code == code);

    private static HardTriggerResult Trigger(DefenseStrategyResult result, HardTriggerCode code) =>
        Assert.Single(result.HardTriggers, trigger => trigger.Code == code);

    private static DrsResult AvailableDrs(double score) => new(EvaluationValueStatus.Available, score,
        DrsClassification.Safe, [], [], "Test");

    private static DrsResult UnavailableDrs() => new(EvaluationValueStatus.InsufficientData, null,
        null, [], [DefenseMissingInputCode.CurrentDelta], "Test");

    private static DefenseEvaluationInput Input(decimal? openingPremium = 1m, decimal? ask = .25m,
        double? delta = .10, double? previousDelta = .10, decimal? underlyingPrice = 80m,
        int dte = 30, decimal strike = 100m, bool includePreviousObservation = true,
        double? currentCcos = null, DateTimeOffset? timestamp = null)
    {
        var evaluationTimestamp = timestamp ?? new DateTimeOffset(2026, 9, 20, 16, 0, 0, TimeSpan.Zero);
        var evaluationDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(evaluationTimestamp,
            TimeZoneInfo.FindSystemTimeZoneById("America/New_York")).DateTime);
        var expiration = evaluationDate.AddDays(dte);
        var position = Position(openingPremium, 1, strike, expiration);
        var version = new ConfigurationVersion(1);
        var current = Observation(expiration, strike, ask, delta, underlyingPrice, evaluationTimestamp.AddMinutes(-1));
        var previous = includePreviousObservation
            ? Observation(expiration, strike, ask, previousDelta, underlyingPrice, evaluationTimestamp.AddDays(-1))
            : null;
        return new DefenseEvaluationInput(position,
            new DefenseHoldingContext(position.HoldingId, "MSFT", AssetType.Stock,
                AssignmentSensitivity.Level3, TaxSensitivity.Moderate), current, previous, currentCcos,
            new EarningsContext(AvailabilityStatus.Unavailable, null), evaluationTimestamp,
            Defense(), Roll(), version, new DefenseStrategyVersion("6.0.0"), new RollStrategyVersion("6.0.0"));
    }

    private static OpenShortCallPositionSnapshot Position(decimal? openingPremium, int contracts = 1,
        decimal strike = 100m, DateOnly? expiration = null) => new(42, Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
        "MSFT-C", contracts, strike, expiration ?? new DateOnly(2026, 10, 20), openingPremium, null);

    private static DefenseOptionObservation Observation(DateOnly expiration, decimal strike, decimal? ask,
        double? delta, decimal? underlyingPrice, DateTimeOffset timestamp) => new("MSFT-C", "MSFT", timestamp,
        expiration, strike, OptionContractType.Call, .20m, ask, .22m, 500, .30, delta, .02, -.01, .10,
        underlyingPrice, "Test");

    private static DefenseConfiguration Defense() => new() { Version = new ConfigurationVersion(1) };

    private static RollConfiguration Roll() => new()
    {
        Version = new ConfigurationVersion(1),
        MaximumRollDebitPerShare = 0
    };
}
