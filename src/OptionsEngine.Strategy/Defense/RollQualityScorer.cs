using System.Collections.Immutable;
using OptionsEngine.Strategy.EntryStrategy;

namespace OptionsEngine.Strategy.Defense;

/// <summary>Pure Phase 6D Roll Quality Score calculation.</summary>
public sealed class RollQualityScorer
{
    private const int MinimumScoredDte = 21;
    private const int MaximumScoredDte = 60;

    public RqsResult Evaluate(RollQualityScoringInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Configuration);
        input.Configuration.Validate();

        var components = ImmutableArray.Create(
            DrsReduction(input),
            DeltaReduction(input),
            StrikeImprovement(input),
            RollEconomics(input),
            ReplacementLiquidity(input),
            TimeEfficiency(input));
        var missing = components.SelectMany(component => component.MissingInputs)
            .Distinct().ToImmutableArray();
        if (components.Any(component => component.Status != EvaluationValueStatus.Available))
            return new RqsResult(EvaluationValueStatus.InsufficientData, null, components, missing,
                "RQS is unavailable because every component is required and missing components are not reweighted.");

        var score = components.Sum(component => component.Score!.Value);
        if (!double.IsFinite(score) || score is < 0 or > 100)
            throw new InvalidOperationException("The calculated RQS is outside zero through 100.");
        return new RqsResult(EvaluationValueStatus.Available, score, components, [],
            "RQS is the sum of all six required Phase 6D components.");
    }

    private static RqsComponentResult DrsReduction(RollQualityScoringInput input)
    {
        var current = ValidScore(input.CurrentDrs) && input.CurrentDrs > 0 ? input.CurrentDrs : null;
        var projected = ValidScore(input.ProjectedDrs) ? input.ProjectedDrs : null;
        var reduction = current is { } currentValue && projected is { } projectedValue
            ? currentValue - projectedValue : (double?)null;
        var ratio = reduction is { } reductionValue && current is { } denominator
            ? Math.Min(Math.Max(reductionValue, 0) / denominator, 1) : (double?)null;
        var values = Values(("CurrentDrs", input.CurrentDrs), ("ProjectedDrs", input.ProjectedDrs),
            ("DrsReduction", reduction), ("DrsReductionRatio", ratio));
        var missing = ImmutableArray.CreateBuilder<RollMissingInputCode>();
        if (current is null) missing.Add(RollMissingInputCode.CurrentDrs);
        if (projected is null) missing.Add(RollMissingInputCode.ProjectedDrs);
        return missing.Count > 0
            ? Unavailable(RqsComponentCode.DrsReduction, 30, values, missing.ToImmutable(),
                "DRS reduction requires a positive current DRS and an available projected DRS.")
            : Available(RqsComponentCode.DrsReduction, 30 * ratio!.Value, 30, values,
                "DRS reduction scores the proportional reduction from current to projected DRS.");
    }

    private static RqsComponentResult DeltaReduction(RollQualityScoringInput input)
    {
        var current = ValidDelta(input.CurrentDelta) && input.CurrentDelta > 0 ? input.CurrentDelta : null;
        var replacement = ValidDelta(input.NewDelta) ? input.NewDelta : null;
        var reduction = current is { } currentValue && replacement is { } replacementValue
            ? currentValue - replacementValue : (double?)null;
        var ratio = reduction is { } reductionValue && current is { } denominator
            ? Math.Min(Math.Max(reductionValue, 0) / denominator, 1) : (double?)null;
        var values = Values(("CurrentDelta", input.CurrentDelta), ("NewDelta", input.NewDelta),
            ("DeltaReduction", reduction), ("DeltaReductionRatio", ratio));
        var missing = ImmutableArray.CreateBuilder<RollMissingInputCode>();
        if (current is null) missing.Add(RollMissingInputCode.ExistingDelta);
        if (replacement is null) missing.Add(RollMissingInputCode.CandidateDelta);
        return missing.Count > 0
            ? Unavailable(RqsComponentCode.DeltaReduction, 20, values, missing.ToImmutable(),
                "Delta reduction requires a positive current Delta and a valid replacement Delta.")
            : Available(RqsComponentCode.DeltaReduction, 20 * ratio!.Value, 20, values,
                "Delta reduction scores the proportional reduction in call Delta without absolute-value normalization.");
    }

    private static RqsComponentResult StrikeImprovement(RollQualityScoringInput input)
    {
        var existingValid = input.ExistingStrike is > 0;
        var replacementValid = input.ReplacementStrike is > 0;
        var improvement = existingValid && replacementValid
            ? input.ReplacementStrike!.Value - input.ExistingStrike!.Value : (decimal?)null;
        var ratio = improvement is { } improvementValue
            ? (double)(improvementValue / input.ExistingStrike!.Value) : (double?)null;
        var normalized = ratio is { } ratioValue
            ? Math.Min(Math.Max(ratioValue, 0) / input.Configuration.FullStrikeImprovementRatio, 1)
            : (double?)null;
        var values = Values(("ExistingStrike", Decimal(input.ExistingStrike)),
            ("NewStrike", Decimal(input.ReplacementStrike)),
            ("StrikeImprovement", Decimal(improvement)), ("StrikeImprovementRatio", ratio),
            ("NormalizedStrikeImprovement", normalized));
        var missing = ImmutableArray.CreateBuilder<RollMissingInputCode>();
        if (!existingValid) missing.Add(RollMissingInputCode.ExistingStrike);
        if (!replacementValid) missing.Add(RollMissingInputCode.ReplacementStrike);
        return missing.Count > 0
            ? Unavailable(RqsComponentCode.StrikeImprovement, 20, values, missing.ToImmutable(),
                "Strike improvement requires positive existing and replacement strikes.")
            : Available(RqsComponentCode.StrikeImprovement, 20 * normalized!.Value, 20, values,
                "Strike improvement saturates at the configured full-improvement ratio.");
    }

    private static RqsComponentResult RollEconomics(RollQualityScoringInput input)
    {
        var net = input.NetRollPerShare;
        var rollDebit = net is { } netValue ? Math.Max(-netValue, 0) : input.RollDebitPerShare;
        double? debitUtilization = null;
        double? creditRatio = null;
        double? score = null;
        var missing = ImmutableArray.CreateBuilder<RollMissingInputCode>();
        if (net is null)
            missing.Add(RollMissingInputCode.NetRollPerShare);
        else if (net < 0)
        {
            if (input.Configuration.MaximumRollDebitPerShare <= 0)
                missing.Add(RollMissingInputCode.Configuration);
            else
            {
                debitUtilization = (double)(rollDebit!.Value /
                    input.Configuration.MaximumRollDebitPerShare);
                score = 10 * (1 - Math.Clamp(debitUtilization.Value, 0, 1));
            }
        }
        else if (net == 0)
        {
            debitUtilization = 0;
            score = 10;
        }
        else if (input.ReplacementStoPerShare is not > 0)
            missing.Add(RollMissingInputCode.ReplacementStoPerShare);
        else
        {
            creditRatio = (double)(net.Value / input.ReplacementStoPerShare.Value);
            score = 10 + 5 * Math.Min(Math.Max(creditRatio.Value, 0) /
                input.Configuration.FullCreditEconomicsRatio, 1);
        }

        var values = Values(("NetRollPerShare", Decimal(net)),
            ("RollDebitPerShare", Decimal(rollDebit)),
            ("ReplacementStoPerShare", Decimal(input.ReplacementStoPerShare)),
            ("DebitUtilization", debitUtilization), ("CreditRatio", creditRatio));
        return missing.Count > 0 || score is null
            ? Unavailable(RqsComponentCode.RollEconomics, 15, values, missing.ToImmutable(),
                "Roll economics requires complete economics and a valid non-zero denominator for the applicable formula.")
            : Available(RqsComponentCode.RollEconomics, score.Value, 15, values,
                "Roll economics scores debits from 10 to 0, even at 10, and credits from 10 to 15.");
    }

    private static RqsComponentResult ReplacementLiquidity(RollQualityScoringInput input)
    {
        var eligibility = ContractLiquidityEvaluator.Evaluate(input.CandidateBid, input.CandidateAsk,
            input.CandidateOpenInterest, input.Configuration.LiquidityEligibility);
        var missing = eligibility.MissingInputs.Select(value => value switch
        {
            ContractLiquidityMissingInput.Bid => RollMissingInputCode.CandidateBid,
            ContractLiquidityMissingInput.Ask => RollMissingInputCode.CandidateAsk,
            ContractLiquidityMissingInput.OpenInterest => RollMissingInputCode.CandidateOpenInterest,
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        }).ToImmutableArray();
        var values = Values(("BidAskSpreadPercent", eligibility.BidAskSpreadPercent),
            ("OpenInterest", input.CandidateOpenInterest));
        if (eligibility.Status != ContractLiquidityEligibilityStatus.Passed ||
            eligibility.BidAskSpreadPercent is not { } spread ||
            input.CandidateOpenInterest is not { } openInterest)
            return Unavailable(RqsComponentCode.ReplacementLiquidity, 10, values, missing,
                "Replacement liquidity requires a candidate that passes the shared Phase 4 liquidity gate.");

        var liquidity = ContractLiquidityScorer.Score(spread, openInterest,
            input.Configuration.LiquidityScoring);
        values = values.SetItem("LiquidityScore", liquidity.Score)
            .SetItem("SpreadScore", liquidity.SpreadScore)
            .SetItem("OpenInterestScore", liquidity.OpenInterestScore);
        return Available(RqsComponentCode.ReplacementLiquidity, liquidity.Score, 10, values,
            "Replacement liquidity reuses the Phase 4 spread and open-interest score.");
    }

    private static RqsComponentResult TimeEfficiency(RollQualityScoringInput input)
    {
        var dte = input.NewDte;
        var valid = dte is { } value && value is >= MinimumScoredDte and <= MaximumScoredDte;
        double? ratio = valid
            ? (double)(MaximumScoredDte - dte!.Value) /
              (MaximumScoredDte - MinimumScoredDte)
            : null;
        var values = Values(("NewDte", dte), ("TimeEfficiencyRatio", ratio));
        return valid
            ? Available(RqsComponentCode.TimeEfficiency, 5 * ratio!.Value, 5, values,
                "Time efficiency rewards eligible replacement candidates with fewer calendar DTE.")
            : Unavailable(RqsComponentCode.TimeEfficiency, 5, values,
                [RollMissingInputCode.NewDte], "Time efficiency requires an eligible replacement DTE.");
    }

    private static RqsComponentResult Available(RqsComponentCode code, double score, double maximum,
        ImmutableDictionary<string, double?> values, string explanation) =>
        new(code, EvaluationValueStatus.Available, score, maximum, values, [], explanation);

    private static RqsComponentResult Unavailable(RqsComponentCode code, double maximum,
        ImmutableDictionary<string, double?> values, ImmutableArray<RollMissingInputCode> missing,
        string explanation) => new(code, EvaluationValueStatus.InsufficientData, null, maximum,
            values, missing, explanation);

    private static ImmutableDictionary<string, double?> Values(
        params (string Name, double? Value)[] values) =>
        values.ToImmutableDictionary(value => value.Name, value => value.Value, StringComparer.Ordinal);

    private static bool ValidScore(double? value) =>
        value is { } score && double.IsFinite(score) && score is >= 0 and <= 100;

    private static bool ValidDelta(double? value) =>
        value is { } delta && double.IsFinite(delta) && delta is >= 0 and <= 1;

    private static double? Decimal(decimal? value) => value is { } number ? (double)number : null;
}
