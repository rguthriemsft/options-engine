using System.Collections.Immutable;

namespace OptionsEngine.Strategy.Defense;

/// <summary>Pure Phase 6D analytical disposition state machine.</summary>
public sealed class DefenseDispositionEvaluator
{
    public DefenseDispositionResult Evaluate(DefenseDispositionEvaluationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.CurrentDefense);
        ArgumentNullException.ThrowIfNull(input.RollConfiguration);
        input.RollConfiguration.Validate();

        return input.CurrentDefense.RollEngineRequired
            ? EvaluateDefensive(input)
            : EvaluateWithoutDefense(input.CurrentDefense);
    }

    private static DefenseDispositionResult EvaluateWithoutDefense(DefenseStrategyResult defense)
    {
        if (defense.HardDefenseStatus == HardDefenseStatus.PartiallyEvaluated)
            return Result(DefenseDisposition.DefenseReview,
                [DefenseReasonCode.NoDefenseActivation, DefenseReasonCode.InsufficientData],
                defense.HardTriggers.SelectMany(trigger => trigger.MissingInputs).Distinct(),
                "Partially evaluated hard defense cannot resolve to a non-defensive disposition when DRS did not activate defense.");

        if (defense.ProfitTaking.Status != EvaluationValueStatus.Available)
            return Result(DefenseDisposition.DefenseReview,
                [DefenseReasonCode.NoDefenseActivation, DefenseReasonCode.InsufficientData],
                defense.ProfitTaking.MissingInputs,
                "Profit-taking disposition is unavailable because required inputs are missing.");

        if (defense.Drs.Status != EvaluationValueStatus.Available &&
            defense.ProfitTaking.Signal == ProfitTakingSignal.None)
            return Result(DefenseDisposition.DefenseReview,
                [DefenseReasonCode.NoDefenseActivation, DefenseReasonCode.InsufficientData],
                defense.Drs.MissingInputs,
                "Unavailable DRS is preserved as review context rather than treated as low risk.");

        return defense.ProfitTaking.Signal switch
        {
            ProfitTakingSignal.StrongCloseCandidate or ProfitTakingSignal.CloseCandidate =>
                Result(DefenseDisposition.ProfitClose,
                    [DefenseReasonCode.NoDefenseActivation, DefenseReasonCode.ProfitTaking], [],
                    "Available profit-taking state supports closing the short call."),
            ProfitTakingSignal.Monitor => Result(DefenseDisposition.Monitor,
                [DefenseReasonCode.NoDefenseActivation], [],
                "Available profit-taking state supports monitoring the position."),
            _ => Result(DefenseDisposition.NoAction,
                [DefenseReasonCode.NoDefenseActivation], [],
                "No defensive activation or profit-taking action is present.")
        };
    }

    private static DefenseDispositionResult EvaluateDefensive(DefenseDispositionEvaluationInput input)
    {
        if (input.RollCandidates is null)
            throw new ArgumentException("Activated defense requires evaluated roll candidates.",
                nameof(input.RollCandidates));

        var activationReasons = ImmutableArray.CreateBuilder<DefenseReasonCode>();
        if (input.CurrentDefense.HardDefenseStatus == HardDefenseStatus.Triggered)
            activationReasons.Add(DefenseReasonCode.HardTriggerActivation);
        if (input.CurrentDefense.Drs is { Status: EvaluationValueStatus.Available, Score: { } drs } &&
            drs >= input.RollConfiguration.ActivationDrs)
            activationReasons.Add(DefenseReasonCode.DrsActivation);

        if (input.RollCandidates.Candidates.Any(candidate =>
                candidate.State == RollCandidateEvaluationState.Rankable))
        {
            if (!ValidCcos(input.CurrentCcos))
                return Result(DefenseDisposition.DefenseReview,
                    activationReasons.Append(DefenseReasonCode.InsufficientData),
                    [DefenseMissingInputCode.CurrentCcos],
                    "A valid current CCOS is required to choose between Roll and CloseWait.");
            if (input.CurrentCcos >= input.RollConfiguration.CurrentCcosRollThreshold)
                return Result(DefenseDisposition.Roll, activationReasons, [],
                    "At least one candidate is Rankable and current CCOS meets the configured Roll threshold.");
            return Result(DefenseDisposition.CloseWait,
                activationReasons.Append(DefenseReasonCode.CurrentCcosBelowRollThreshold), [],
                "A Rankable candidate exists, but current CCOS is below the configured Roll threshold.");
        }

        if (input.RollCandidates.Candidates.Any(candidate =>
                candidate.State == RollCandidateEvaluationState.InsufficientData))
            return Result(DefenseDisposition.DefenseReview,
                activationReasons.Append(DefenseReasonCode.NoEligibleRollCandidate)
                    .Append(DefenseReasonCode.InsufficientData), [],
                "No candidate is Rankable and at least one candidate remains insufficiently evaluated.");

        if (input.CurrentDefense.HardDefenseStatus == HardDefenseStatus.Triggered)
            return Result(DefenseDisposition.DefenseReview,
                activationReasons.Append(DefenseReasonCode.NoEligibleRollCandidate), [],
                "Hard defense is triggered and no Rankable replacement candidate exists.");

        return Result(DefenseDisposition.CloseWait,
            activationReasons.Append(DefenseReasonCode.NoEligibleRollCandidate), [],
            "DRS-only activation has no Rankable replacement candidate.");
    }

    private static bool ValidCcos(double? value) =>
        value is { } score && double.IsFinite(score) && score is >= 0 and <= 100;

    private static DefenseDispositionResult Result(DefenseDisposition disposition,
        IEnumerable<DefenseReasonCode> reasons, IEnumerable<DefenseMissingInputCode> missing,
        string explanation) => new(disposition, reasons.Distinct().ToImmutableArray(),
        missing.Distinct().ToImmutableArray(), [explanation]);
}
