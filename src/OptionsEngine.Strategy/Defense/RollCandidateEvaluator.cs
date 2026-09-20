using System.Collections.Immutable;
using OptionsEngine.Domain.Accounts;
using OptionsEngine.Strategy.EntryStrategy;

namespace OptionsEngine.Strategy.Defense;

/// <summary>Pure Phase 6C hard-gate, economics, and projected-DRS evaluation.</summary>
public sealed class RollCandidateEvaluator
{
    private const int ContractMultiplier = 100;

    public RollCandidateStrategyResult Evaluate(RollCandidateEvaluationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        input.Validate();

        var candidates = input.SelectedChainSnapshots
            .SelectMany(chain => chain.Contracts)
            .Select(candidate => EvaluateCandidate(input, candidate))
            .ToImmutableArray();
        return new RollCandidateStrategyResult(input.SelectedChainSnapshots, candidates);
    }

    private static RollCandidateEvaluation EvaluateCandidate(RollCandidateEvaluationInput input,
        DefenseOptionObservation candidate)
    {
        var reasons = ImmutableArray.CreateBuilder<RollReasonCode>();
        var missing = ImmutableArray.CreateBuilder<RollMissingInputCode>();
        var explanations = ImmutableArray.CreateBuilder<string>();
        var configuration = input.RollConfiguration;
        var evaluationDate = input.CurrentDefense.DefenseEvaluationDate;
        var newDte = candidate.Expiration.DayNumber - evaluationDate.DayNumber;
        var additionalDte = candidate.Expiration.DayNumber - input.Position.Expiration.DayNumber;
        var replacementDteEligible = newDte >= configuration.MinimumReplacementDte &&
                                     newDte <= configuration.MaximumReplacementDte;
        var dteWindow = newDte >= configuration.PreferredMinimumDte &&
                        newDte <= configuration.PreferredMaximumDte
            ? ReplacementDteWindow.Preferred
            : replacementDteEligible
                ? ReplacementDteWindow.Extended
                : (ReplacementDteWindow?)null;
        if (candidate.OptionType != OptionContractType.Call)
            Reject(RollReasonCode.OptionTypeNotCall, "V1 replacement candidates must be calls.");
        if (!replacementDteEligible)
            Reject(RollReasonCode.DteOutsideRange, "Replacement DTE is outside the configured range.");
        if (candidate.Expiration <= input.Position.Expiration)
            Reject(RollReasonCode.ExpirationNotImproved, "Replacement expiration must be later than the existing expiration.");

        var strikeImprovement = candidate.Strike - input.Position.Strike;
        var strikeImprovementRatio = (double)(strikeImprovement / input.Position.Strike);
        if (candidate.Strike <= input.Position.Strike)
            Reject(RollReasonCode.StrikeNotImproved, "Replacement strike must be strictly higher than the existing strike.");
        var underlyingPrice = input.CurrentOptionObservation?.UnderlyingPrice;
        if (underlyingPrice is null or <= 0)
            Missing(RollMissingInputCode.UnderlyingPrice,
                "A positive current underlying price is required for the strict-OTM gate.");
        else if (candidate.Strike <= underlyingPrice)
            Reject(RollReasonCode.StrikeNotStrictlyOtm,
                "Replacement strike must be strictly above the current underlying price.");

        var candidateDelta = ValidDelta(candidate.Delta);
        var currentDelta = ValidDelta(input.CurrentOptionObservation?.Delta);
        bool? preferredDeltaWindow = candidateDelta is { } newDelta
            ? newDelta >= configuration.PreferredMinimumDelta && newDelta <= configuration.PreferredMaximumDelta
            : null;
        double? deltaReduction = candidateDelta is { } validCandidateDelta && currentDelta is { } validCurrentDelta
            ? validCurrentDelta - validCandidateDelta
            : null;
        if (candidateDelta is null)
            Missing(RollMissingInputCode.CandidateDelta,
                "Candidate Delta must be finite and within zero through one.");
        if (currentDelta is null)
            Missing(RollMissingInputCode.ExistingDelta,
                "A valid current Delta is required for the strict reduction gate.");
        if (candidateDelta is { } comparableCandidate && currentDelta is { } comparableCurrent &&
            comparableCandidate >= comparableCurrent)
            Reject(RollReasonCode.DeltaNotReduced,
                "Replacement Delta must be strictly lower than current Delta.");
        var maximumDelta = input.Holding.TaxSensitivity == TaxSensitivity.High
            ? configuration.HighTaxMaximumDelta
            : configuration.NormalMaximumDelta;
        if (candidateDelta is { } boundedDelta && boundedDelta > maximumDelta)
            Reject(RollReasonCode.DeltaExceedsMaximum,
                "Replacement Delta exceeds the effective configured maximum.");

        var liquidity = ContractLiquidityEvaluator.Evaluate(candidate.Bid, candidate.Ask,
            candidate.OpenInterest, configuration.LiquidityEligibility);
        foreach (var liquidityMissing in liquidity.MissingInputs)
            Missing(liquidityMissing switch
            {
                ContractLiquidityMissingInput.Bid => RollMissingInputCode.CandidateBid,
                ContractLiquidityMissingInput.Ask => RollMissingInputCode.CandidateAsk,
                ContractLiquidityMissingInput.OpenInterest => RollMissingInputCode.CandidateOpenInterest,
                _ => throw new ArgumentOutOfRangeException(nameof(liquidityMissing))
            }, "Candidate liquidity requires Bid, Ask, and OpenInterest.");
        if (liquidity.Status == ContractLiquidityEligibilityStatus.Failed)
            Reject(RollReasonCode.InsufficientLiquidity,
                "Candidate failed the shared Phase 4 liquidity hard gate.");

        if (input.Holding.AssetType != AssetType.ExchangeTradedFund)
        {
            if (input.Earnings.Status != AvailabilityStatus.Available ||
                input.Earnings.NextEarningsDate is null)
                Missing(RollMissingInputCode.EarningsDate,
                    "A known next earnings date is required for a non-ETF candidate.");
            else if (input.Position.Expiration < input.Earnings.NextEarningsDate &&
                     input.Earnings.NextEarningsDate <= candidate.Expiration)
                Reject(RollReasonCode.EarningsCrossing,
                    "Replacement introduces a new known earnings crossing.");
        }

        var existingAsk = input.CurrentOptionObservation?.Ask;
        var replacementBid = candidate.Bid;
        if (existingAsk is null or <= 0)
            Missing(RollMissingInputCode.ExistingAsk,
                "Existing BTC economics requires a positive current Ask.");
        if (replacementBid is null or <= 0)
            Missing(RollMissingInputCode.CandidateBid,
                "Replacement STO economics requires a positive candidate Bid.");
        decimal? netRollPerShare = existingAsk is > 0 && replacementBid is > 0
            ? replacementBid.Value - existingAsk.Value
            : null;
        decimal? netRollTotal = netRollPerShare is { } net
            ? net * ContractMultiplier * input.Position.Contracts
            : null;
        decimal? rollDebitPerShare = netRollPerShare is { } netPerShare
            ? Math.Max(-netPerShare, 0)
            : null;
        if (netRollPerShare is { } economics && economics < -configuration.MaximumRollDebitPerShare)
            Reject(RollReasonCode.DebitExceedsMaximum,
                "Net roll debit exceeds the configured absolute maximum.");

        var projectedPosition = input.Position with
        {
            OptionSymbol = candidate.OptionSymbol,
            Strike = candidate.Strike,
            Expiration = candidate.Expiration,
            OpeningPremiumPerShare = candidate.Bid
        };
        var projectedObservation = candidate with
        {
            Bid = candidate.Bid,
            Ask = candidate.Bid,
            UnderlyingPrice = underlyingPrice
        };
        var projectedDrs = DefenseEvaluator.EvaluateDrs(projectedPosition, projectedObservation,
            input.DefenseEvaluationTimestampUtc, input.DrsConfiguration);
        var projectedScore = AvailableDrs(projectedDrs);
        var currentScore = AvailableDrs(input.CurrentDefense.Drs);
        if (projectedScore is null)
            Missing(RollMissingInputCode.ProjectedDrs,
                "Projected DRS requires every projected Phase 6B component.");
        if (currentScore is null)
            Missing(RollMissingInputCode.CurrentDrs,
                "An available current DRS is required for strict projected improvement.");
        double? drsReduction = projectedScore is { } projected && currentScore is { } current
            ? current - projected
            : null;
        if (projectedScore is { } candidateScore && currentScore is { } existingScore &&
            candidateScore >= existingScore)
            Reject(RollReasonCode.ProjectedDrsNotImproved,
                "Projected DRS must be strictly lower than current DRS.");
        if (projectedScore is { } maximumCompared && maximumCompared >= configuration.MaximumProjectedDrs)
            Reject(RollReasonCode.ProjectedDrsExceedsMaximum,
                "Projected DRS must be strictly below the configured maximum.");

        var reasonCodes = reasons.Distinct().ToImmutableArray();
        var missingInputs = missing.Distinct().ToImmutableArray();
        if (missingInputs.Length > 0 && !reasonCodes.Contains(RollReasonCode.InsufficientData))
            reasonCodes = reasonCodes.Add(RollReasonCode.InsufficientData);
        var state = reasons.Count > 0
            ? RollCandidateEvaluationState.Rejected
            : missingInputs.Length > 0
                ? RollCandidateEvaluationState.InsufficientData
                : RollCandidateEvaluationState.EligibleForRqs;
        var metrics = new RollCandidateDerivedMetrics(newDte, additionalDte, dteWindow,
            preferredDeltaWindow, strikeImprovement, strikeImprovementRatio, deltaReduction,
            liquidity.BidAskSpreadPercent, input.Position.Contracts,
            existingAsk is > 0 ? existingAsk : null, replacementBid is > 0 ? replacementBid : null,
            netRollPerShare, netRollTotal, rollDebitPerShare, projectedDrs, drsReduction);
        return new RollCandidateEvaluation(candidate, state, metrics, reasonCodes, missingInputs,
            null, null, explanations.ToImmutable());

        void Reject(RollReasonCode reason, string explanation)
        {
            reasons.Add(reason);
            explanations.Add(explanation);
        }

        void Missing(RollMissingInputCode missingInput, string explanation)
        {
            missing.Add(missingInput);
            explanations.Add(explanation);
        }
    }

    private static double? ValidDelta(double? delta) =>
        delta is { } value && double.IsFinite(value) && value is >= 0 and <= 1 ? value : null;

    private static double? AvailableDrs(DrsResult? drs) =>
        drs is { Status: EvaluationValueStatus.Available, Score: { } score } &&
        double.IsFinite(score) && score is >= 0 and <= 100
            ? score
            : null;
}
