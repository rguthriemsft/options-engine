using System.Collections.Immutable;

namespace OptionsEngine.Strategy.Defense;

/// <summary>Pure Phase 6B evaluation of one current open short-call position.</summary>
public sealed class DefenseEvaluator
{
    private const int ContractMultiplier = 100;
    private const double FloatingComparisonTolerance = 1e-12;
    private static readonly TimeZoneInfo NewYorkTimeZone =
        TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    public DefenseStrategyResult Evaluate(DefenseEvaluationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        input.Validate();

        var evaluationDate = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(input.DefenseEvaluationTimestampUtc, NewYorkTimeZone).DateTime);
        var dte = input.Position.Expiration.DayNumber - evaluationDate.DayNumber;
        var currentDelta = ValidDelta(input.CurrentOptionObservation?.Delta);
        var previousDelta = ValidDelta(input.PreviousDeltaObservation?.Delta);
        double? deltaVelocity = currentDelta is { } current && previousDelta is { } previous
            ? current - previous
            : null;
        var strikeDistanceRatio = StrikeDistanceRatio(input);

        var profitTaking = EvaluateProfitTaking(input);
        var drs = EvaluateDrs(input, dte, currentDelta, strikeDistanceRatio);
        var hardTriggers = EvaluateHardTriggers(input, dte, currentDelta, previousDelta,
            deltaVelocity, strikeDistanceRatio);
        var hardDefenseStatus = AggregateHardDefenseStatus(hardTriggers);
        var rollEngineRequired = RequiresRollEngine(drs, hardDefenseStatus, input.RollConfiguration);

        return new DefenseStrategyResult(evaluationDate, dte, profitTaking, drs, deltaVelocity,
            hardTriggers, hardDefenseStatus, rollEngineRequired);
    }

    public static DrsClassification Classify(double score, DrsConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        configuration.Validate();
        if (!double.IsFinite(score) || score < 0 || score > 100)
            throw new ArgumentOutOfRangeException(nameof(score));

        return configuration.ClassificationBands
            .First(band => Contains(band.Minimum, band.IncludesMinimum, band.Maximum,
                band.IncludesMaximum, score)).Classification;
    }

    public static HardDefenseStatus AggregateHardDefenseStatus(IEnumerable<HardTriggerResult> triggers)
    {
        ArgumentNullException.ThrowIfNull(triggers);
        var results = triggers.ToArray();
        if (results.Any(result => result.Status == HardTriggerStatus.Triggered))
            return HardDefenseStatus.Triggered;

        var applicable = results.Where(result => result.Status != HardTriggerStatus.NotApplicable).ToArray();
        return applicable.Length > 0 && applicable.All(result => result.Status == HardTriggerStatus.NotTriggered)
            ? HardDefenseStatus.Clear
            : HardDefenseStatus.PartiallyEvaluated;
    }

    public static bool RequiresRollEngine(DrsResult drs, HardDefenseStatus hardDefenseStatus,
        RollConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(drs);
        ArgumentNullException.ThrowIfNull(configuration);
        return hardDefenseStatus == HardDefenseStatus.Triggered ||
               drs.Status == EvaluationValueStatus.Available &&
               drs.Score is { } score && score >= configuration.ActivationDrs;
    }

    private static ProfitTakingResult EvaluateProfitTaking(DefenseEvaluationInput input)
    {
        var missing = ImmutableArray.CreateBuilder<DefenseMissingInputCode>();
        var openingPremium = input.Position.OpeningPremiumPerShare;
        var currentAsk = input.CurrentOptionObservation?.Ask;
        if (openingPremium is null or <= 0) missing.Add(DefenseMissingInputCode.OpeningPremiumPerShare);
        if (currentAsk is null or <= 0) missing.Add(DefenseMissingInputCode.CurrentAsk);

        if (missing.Count > 0)
            return new ProfitTakingResult(EvaluationValueStatus.InsufficientData, ProfitTakingSignal.None,
                null, null, null, null, missing.ToImmutable(),
                "Profit taking requires a positive opening premium and current Ask.");

        var grossOpeningPremium = openingPremium!.Value * ContractMultiplier * input.Position.Contracts;
        var estimatedCurrentBtcCost = currentAsk!.Value * ContractMultiplier * input.Position.Contracts;
        var grossPremiumCaptured = grossOpeningPremium - estimatedCurrentBtcCost;
        var capturedRatio = (double)((openingPremium.Value - currentAsk.Value) / openingPremium.Value);
        var configuration = input.DefenseConfiguration.ProfitTaking;
        var signal = capturedRatio >= configuration.StrongCloseRatio
            ? ProfitTakingSignal.StrongCloseCandidate
            : capturedRatio >= configuration.CloseRatio
                ? ProfitTakingSignal.CloseCandidate
                : capturedRatio >= configuration.MonitorRatio
                    ? ProfitTakingSignal.Monitor
                    : ProfitTakingSignal.None;

        return new ProfitTakingResult(EvaluationValueStatus.Available, signal, grossOpeningPremium,
            estimatedCurrentBtcCost, grossPremiumCaptured, capturedRatio, [],
            "Gross captured premium uses the current Ask as the BTC reference price.");
    }

    private static DrsResult EvaluateDrs(DefenseEvaluationInput input, int dte, double? currentDelta,
        double? strikeDistanceRatio)
    {
        var configuration = input.DefenseConfiguration.Drs;
        var components = ImmutableArray.Create(
            ScoreComponent(DrsComponentCode.Delta, currentDelta, 40,
                currentDelta is null ? DefenseMissingInputCode.CurrentDelta : null,
                configuration.DeltaBands, "Current normalized call Delta."),
            ScoreComponent(DrsComponentCode.StrikeProximity, strikeDistanceRatio, 25,
                strikeDistanceRatio is null ? DefenseMissingInputCode.UnderlyingPrice : null,
                configuration.StrikeProximityBands, "Strike distance relative to the current underlying price."),
            ScoreComponent(DrsComponentCode.Dte, dte >= 0 ? dte : null, 15,
                dte < 0 ? DefenseMissingInputCode.Dte : null,
                configuration.DteBands, "Calendar DTE using the America/New_York evaluation date."),
            PremiumExpansionComponent(input, configuration));

        var missing = components.SelectMany(component => component.MissingInputs).Distinct().ToImmutableArray();
        if (components.Any(component => component.Status != EvaluationValueStatus.Available))
            return new DrsResult(EvaluationValueStatus.InsufficientData, null, null, components, missing,
                "Complete DRS is unavailable because at least one required component is unavailable.");

        var score = components.Sum(component => component.Score!.Value);
        if (score is < 0 or > 100)
            throw new InvalidOperationException("The configured DRS produced a score outside 0 through 100.");
        return new DrsResult(EvaluationValueStatus.Available, score, Classify(score, configuration),
            components, [], "DRS is the unweighted sum of the four configured Phase 6 V1 component scores.");
    }

    private static ExplanationComponent PremiumExpansionComponent(DefenseEvaluationInput input,
        DrsConfiguration configuration)
    {
        var missing = ImmutableArray.CreateBuilder<DefenseMissingInputCode>();
        var openingPremium = input.Position.OpeningPremiumPerShare;
        var currentAsk = input.CurrentOptionObservation?.Ask;
        if (openingPremium is null or <= 0) missing.Add(DefenseMissingInputCode.OpeningPremiumPerShare);
        if (currentAsk is null or <= 0) missing.Add(DefenseMissingInputCode.CurrentAsk);
        if (missing.Count > 0)
            return new ExplanationComponent(DrsComponentCode.PremiumExpansion,
                EvaluationValueStatus.InsufficientData, null, null, 20, missing.ToImmutable(),
                "Premium expansion requires a positive opening premium and current Ask.");

        var multiple = (double)(currentAsk!.Value / openingPremium!.Value);
        return ScoreComponent(DrsComponentCode.PremiumExpansion, multiple, 20, null,
            configuration.PremiumExpansionBands, "Current Ask divided by opening premium per share.");
    }

    private static ExplanationComponent ScoreComponent(DrsComponentCode code, double? observedValue,
        double maximumScore, DefenseMissingInputCode? missingInput, ImmutableArray<DefenseScoreBand> bands,
        string explanation)
    {
        if (observedValue is null)
            return new ExplanationComponent(code, EvaluationValueStatus.InsufficientData, null, null,
                maximumScore, missingInput is { } missing ? [missing] : [], explanation);

        var band = bands.First(candidate => Contains(candidate.Minimum, candidate.IncludesMinimum,
            candidate.Maximum, candidate.IncludesMaximum, observedValue.Value));
        return new ExplanationComponent(code, EvaluationValueStatus.Available, observedValue, band.Score,
            maximumScore, [], explanation);
    }

    private static ImmutableArray<HardTriggerResult> EvaluateHardTriggers(DefenseEvaluationInput input, int dte,
        double? currentDelta, double? previousDelta, double? deltaVelocity, double? strikeDistanceRatio)
    {
        var configuration = input.DefenseConfiguration.HardTriggers;
        return
        [
            ThresholdTrigger(HardTriggerCode.HighDelta, "CurrentDelta", currentDelta,
                configuration.HighDelta, DefenseMissingInputCode.CurrentDelta,
                "Current Delta is at or above the high-Delta threshold."),
            StrikeProximityTrigger(currentDelta, strikeDistanceRatio, configuration),
            InTheMoneyTrigger(input),
            LowDteTrigger(dte, currentDelta, configuration),
            RapidDeltaTrigger(currentDelta, previousDelta, deltaVelocity, configuration)
        ];
    }

    private static HardTriggerResult StrikeProximityTrigger(double? currentDelta, double? distance,
        HardTriggerConfiguration configuration)
    {
        var observed = Observed(("StrikeDistanceRatio", distance), ("CurrentDelta", currentDelta));
        if (distance is null)
            return Trigger(HardTriggerCode.StrikeProximityWithDelta, HardTriggerStatus.InsufficientData,
                observed, [DefenseMissingInputCode.UnderlyingPrice],
                "Strike proximity requires a positive current underlying price.");
        if (distance < 0)
            return Trigger(HardTriggerCode.StrikeProximityWithDelta, HardTriggerStatus.NotApplicable,
                observed, [], "The proximity trigger applies only to ATM/OTM positions; ITM is evaluated separately.");
        if (distance > configuration.StrikeProximityRatio)
            return Trigger(HardTriggerCode.StrikeProximityWithDelta, HardTriggerStatus.NotTriggered,
                observed, [], "The position is not within the applicable ATM/OTM proximity range.");
        if (currentDelta is null)
            return Trigger(HardTriggerCode.StrikeProximityWithDelta, HardTriggerStatus.InsufficientData,
                observed, [DefenseMissingInputCode.CurrentDelta], "Proximity is known but current Delta is unavailable.");
        return Trigger(HardTriggerCode.StrikeProximityWithDelta,
            currentDelta >= configuration.StrikeProximityMinimumDelta
                ? HardTriggerStatus.Triggered : HardTriggerStatus.NotTriggered,
            observed, [], "ATM/OTM proximity and current Delta were evaluated together.");
    }

    private static HardTriggerResult InTheMoneyTrigger(DefenseEvaluationInput input)
    {
        var underlyingPrice = input.CurrentOptionObservation?.UnderlyingPrice;
        var observed = Observed(("UnderlyingPrice", underlyingPrice is { } price ? (double)price : null),
            ("Strike", (double)input.Position.Strike));
        if (underlyingPrice is null or <= 0)
            return Trigger(HardTriggerCode.InTheMoney, HardTriggerStatus.InsufficientData, observed,
                [DefenseMissingInputCode.UnderlyingPrice], "ITM evaluation requires a positive underlying price.");
        return Trigger(HardTriggerCode.InTheMoney,
            underlyingPrice > input.Position.Strike ? HardTriggerStatus.Triggered : HardTriggerStatus.NotTriggered,
            observed, [], "ITM requires underlying price to be strictly greater than strike.");
    }

    private static HardTriggerResult LowDteTrigger(int dte, double? currentDelta,
        HardTriggerConfiguration configuration)
    {
        var observed = Observed(("Dte", dte), ("CurrentDelta", currentDelta));
        if (dte < 0)
            return Trigger(HardTriggerCode.LowDteWithDelta, HardTriggerStatus.InsufficientData, observed,
                [DefenseMissingInputCode.Dte], "Negative DTE is invalid current-position state.");
        if (dte > configuration.LowDteMaximum)
            return Trigger(HardTriggerCode.LowDteWithDelta, HardTriggerStatus.NotTriggered, observed, [],
                "DTE is above the low-DTE range.");
        if (currentDelta is null)
            return Trigger(HardTriggerCode.LowDteWithDelta, HardTriggerStatus.InsufficientData, observed,
                [DefenseMissingInputCode.CurrentDelta], "Low-DTE evaluation requires current Delta.");
        return Trigger(HardTriggerCode.LowDteWithDelta,
            currentDelta >= configuration.LowDteMinimumDelta
                ? HardTriggerStatus.Triggered : HardTriggerStatus.NotTriggered,
            observed, [], "Low DTE and current Delta were evaluated together.");
    }

    private static HardTriggerResult RapidDeltaTrigger(double? currentDelta, double? previousDelta,
        double? deltaVelocity, HardTriggerConfiguration configuration)
    {
        var missing = ImmutableArray.CreateBuilder<DefenseMissingInputCode>();
        if (currentDelta is null) missing.Add(DefenseMissingInputCode.CurrentDelta);
        if (previousDelta is null) missing.Add(DefenseMissingInputCode.PreviousDelta);
        var observed = Observed(("CurrentDelta", currentDelta), ("PreviousDelta", previousDelta),
            ("DeltaVelocity", deltaVelocity));
        if (missing.Count > 0)
            return Trigger(HardTriggerCode.RapidDeltaIncrease, HardTriggerStatus.InsufficientData,
                observed, missing.ToImmutable(), "Delta velocity requires valid current and previous Deltas.");
        return Trigger(HardTriggerCode.RapidDeltaIncrease,
            deltaVelocity!.Value > configuration.RapidDeltaIncrease ||
            Math.Abs(deltaVelocity.Value - configuration.RapidDeltaIncrease) <= FloatingComparisonTolerance
                ? HardTriggerStatus.Triggered : HardTriggerStatus.NotTriggered,
            observed, [], "Delta velocity is current Delta minus previous trading-day Delta.");
    }

    private static HardTriggerResult ThresholdTrigger(HardTriggerCode code, string observedName,
        double? observedValue, double threshold, DefenseMissingInputCode missingInput, string explanation)
    {
        var observed = Observed((observedName, observedValue));
        return observedValue is null
            ? Trigger(code, HardTriggerStatus.InsufficientData, observed, [missingInput], explanation)
            : Trigger(code, observedValue >= threshold ? HardTriggerStatus.Triggered : HardTriggerStatus.NotTriggered,
                observed, [], explanation);
    }

    private static HardTriggerResult Trigger(HardTriggerCode code, HardTriggerStatus status,
        ImmutableDictionary<string, double?> observedValues, ImmutableArray<DefenseMissingInputCode> missingInputs,
        string explanation) => new(code, status, observedValues, missingInputs, explanation);

    private static ImmutableDictionary<string, double?> Observed(params (string Name, double? Value)[] values) =>
        values.ToImmutableDictionary(value => value.Name, value => value.Value, StringComparer.Ordinal);

    private static double? StrikeDistanceRatio(DefenseEvaluationInput input)
    {
        var underlyingPrice = input.CurrentOptionObservation?.UnderlyingPrice;
        return underlyingPrice is null or <= 0
            ? null
            : (double)((input.Position.Strike - underlyingPrice.Value) / underlyingPrice.Value);
    }

    private static double? ValidDelta(double? delta) =>
        delta is { } value && double.IsFinite(value) && value is >= 0 and <= 1 ? value : null;

    private static bool Contains(double minimum, bool includesMinimum, double maximum, bool includesMaximum,
        double value) =>
        (includesMinimum ? value >= minimum : value > minimum) &&
        (includesMaximum ? value <= maximum : value < maximum);
}
