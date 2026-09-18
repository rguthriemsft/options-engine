using System.Globalization;
using OptionsEngine.Domain.Accounts;
using OptionsEngine.Strategy.Indicators;

namespace OptionsEngine.Strategy.EntryStrategy;

/// <summary>Pure Phase 4C contract gates, scoring, ranking, and entry selection.</summary>
public sealed class ContractStrategyCalculator
{
    private readonly CcosCalculator _ccos = new();

    public ContractStrategyResult Evaluate(EvaluationContext context, IReadOnlyList<OptionContractContext> contracts)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(contracts);
        context.Validate();
        var underlying = _ccos.Evaluate(context);
        var evaluationDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(context.EvaluationTimestampUtc,
            TimeZoneInfo.FindSystemTimeZoneById("America/New_York")).DateTime);

        var evaluated = contracts.Where(x => x.OptionType == OptionContractType.Call)
            .Select(x => EvaluateContract(context, x, evaluationDate)).ToArray();
        evaluated = Rank(evaluated).ToArray();

        var preferred = evaluated.Where(x => x.EntryAcceptable).OrderBy(x => x.Rank).FirstOrDefault();
        var disposition = underlying.BreakoutVeto.Status == GateStatus.Failed
            ? DispositionReasonCode.BreakoutVeto
            : underlying.Ccos.Status != ScoreStatus.Available || underlying.BreakoutVeto.Status == GateStatus.Unavailable
                ? DispositionReasonCode.InsufficientData
                : underlying.Ccos.MeetsConfiguredMinimum == false
                    ? DispositionReasonCode.CcosBelowMinimum
                    : preferred is null ? DispositionReasonCode.NoAcceptableContract : DispositionReasonCode.EntryCandidate;
        if (disposition != DispositionReasonCode.EntryCandidate) preferred = null;
        var missing = underlying.Ccos.MissingInputs.Concat(underlying.BreakoutVeto.MissingInputs)
            .Concat(evaluated.SelectMany(x => x.MissingInputs)).Distinct().ToArray();
        return new ContractStrategyResult(underlying.Ccos, underlying.BreakoutVeto, evaluated,
            preferred is not null, preferred?.Contract.OptionSymbol, preferred?.Contract.Strike,
            preferred?.Contract.Expiration, preferred?.DerivedMetrics.ReferencePremium, disposition, missing,
            [disposition.ToString()]);
    }

    /// <summary>Assigns ranks using only the approved Phase 4C score and tie-break keys.</summary>
    public static IReadOnlyList<ContractEvaluation> Rank(IReadOnlyList<ContractEvaluation> contracts)
    {
        ArgumentNullException.ThrowIfNull(contracts);
        var evaluated = contracts.Select(x => x with { Rank = null }).ToArray();
        var ranked = evaluated.Select((value, index) => (value, index))
            .Where(x => x.value.HardGateEligible && x.value.ContractScore?.Status == ScoreStatus.Available)
            .OrderByDescending(x => x.value.ContractScore!.Score!.Value)
            .ThenBy(x => x.value.Contract.Delta!.Value)
            .ThenByDescending(x => x.value.DerivedMetrics.OtmPercent!.Value)
            .ThenByDescending(x => x.value.DerivedMetrics.AnnualizedPremiumYield!.Value)
            .ThenBy(x => x.value.DerivedMetrics.BidAskSpreadPercent!.Value)
            .ThenByDescending(x => x.value.Contract.OpenInterest!.Value)
            .ThenBy(x => x.value.DerivedMetrics.Dte!.Value)
            .ThenBy(x => x.value.Contract.OptionSymbol, StringComparer.Ordinal)
            .ToArray();
        for (var index = 0; index < ranked.Length; index++)
            evaluated[ranked[index].index] = ranked[index].value with { Rank = index + 1 };
        return evaluated;
    }

    private static ContractEvaluation EvaluateContract(EvaluationContext context, OptionContractContext contract, DateOnly evaluationDate)
    {
        var metrics = Derive(contract, evaluationDate);
        var gates = Gates(context, contract, metrics);
        var eligible = gates.All(x => x.Status is GateStatus.Passed or GateStatus.NotApplicable);
        var score = eligible ? ContractScore(context, contract, metrics) : null;
        var acceptable = eligible && score?.MeetsConfiguredMinimum == true;
        var missing = gates.SelectMany(x => x.MissingInputs).Concat(score?.MissingInputs ?? []).Distinct().ToArray();
        var explanations = gates.Where(x => x.Status != GateStatus.Passed && x.Status != GateStatus.NotApplicable)
            .Select(x => x.Explanation).Concat(score is null ? [] : [score.Explanation]).ToArray();
        return new ContractEvaluation(contract, metrics, gates, score, eligible, acceptable, null, missing, explanations);
    }

    public static ContractDerivedMetrics Derive(OptionContractContext contract, DateOnly evaluationDate)
    {
        ArgumentNullException.ThrowIfNull(contract);
        var dte = contract.Expiration.DayNumber - evaluationDate.DayNumber;
        var premium = contract.Bid;
        var mid = contract.Bid is { } bid && contract.Ask is { } ask ? (bid + ask) / 2m : (decimal?)null;
        var spread = mid is > 0 && contract.Bid is { } spreadBid && contract.Ask is { } spreadAsk
            ? (double)((spreadAsk - spreadBid) / mid.Value) : (double?)null;
        var distance = contract.UnderlyingPrice is > 0 ? contract.Strike - contract.UnderlyingPrice.Value : (decimal?)null;
        var otm = contract.UnderlyingPrice is > 0 && distance is { } strikeDistance
            ? (double)(strikeDistance / contract.UnderlyingPrice.Value) : (double?)null;
        var yield = contract.UnderlyingPrice is > 0 && premium is { } reference
            ? (double)(reference / contract.UnderlyingPrice.Value) : (double?)null;
        var daily = dte > 0 && yield is { } premiumYield ? premiumYield / dte : (double?)null;
        var annualized = daily * 365;
        return new ContractDerivedMetrics(dte, premium, mid, spread, distance, otm, yield, daily, annualized);
    }

    private static IReadOnlyList<GateResult> Gates(EvaluationContext context, OptionContractContext contract, ContractDerivedMetrics metrics)
    {
        var holding = context.Holding;
        var configuration = context.Configuration.ContractEligibility;
        var maximumDelta = EffectiveMaximumDelta(holding, configuration);
        var dte = metrics.Dte!.Value;
        var dteGate = Gate(GateCode.Dte, dte >= configuration.MinimumDte && dte <= configuration.MaximumDte,
            RejectionReasonCode.DteOutsideRange, [Input("DTE", dte)]);

        var priceValid = contract.UnderlyingPrice is > 0;
        var strikeGate = priceValid
            ? Gate(GateCode.StrikeOtM, contract.Strike > contract.UnderlyingPrice!.Value,
                RejectionReasonCode.StrikeNotOtm, [Input("STRIKE", contract.Strike), Input("UNDERLYING_PRICE", contract.UnderlyingPrice.Value)])
            : UnavailableGate(GateCode.StrikeOtM, [Input("STRIKE", contract.Strike), Invalid("UNDERLYING_PRICE", contract.UnderlyingPrice)],
                [MissingInputCode.OptionUnderlyingPrice]);

        var deltaValid = contract.Delta is { } delta && double.IsFinite(delta) && delta is >= 0 and <= 1;
        var deltaGate = deltaValid
            ? Gate(GateCode.MaximumDelta, contract.Delta!.Value <= maximumDelta, RejectionReasonCode.DeltaExceedsMaximum,
                [Input("DELTA", contract.Delta.Value), Input("EFFECTIVE_MAXIMUM_DELTA", maximumDelta)])
            : UnavailableGate(GateCode.MaximumDelta, [Invalid("DELTA", contract.Delta), Input("EFFECTIVE_MAXIMUM_DELTA", maximumDelta)],
                [MissingInputCode.OptionDelta]);

        GateResult earningsGate;
        if (holding.AssetType == AssetType.ExchangeTradedFund && context.Earnings.Status == AvailabilityStatus.NotApplicable)
            earningsGate = new GateResult(GateCode.Earnings, GateStatus.NotApplicable, null, [], [], "ETF earnings is not applicable.");
        else if (context.Earnings.Status != AvailabilityStatus.Available || context.Earnings.NextEarningsDate is null)
            earningsGate = UnavailableGate(GateCode.Earnings, [Invalid("EARNINGS_DATE", context.Earnings.NextEarningsDate)],
                [MissingInputCode.EarningsDate]);
        else
            earningsGate = Gate(GateCode.Earnings, context.Earnings.NextEarningsDate > contract.Expiration,
                RejectionReasonCode.EarningsBeforeExpiration, [Input("EARNINGS_DATE", context.Earnings.NextEarningsDate.Value)]);

        var liquidityMissing = new List<MissingInputCode>();
        if (contract.Bid is null) liquidityMissing.Add(MissingInputCode.OptionBid);
        if (contract.Ask is null) liquidityMissing.Add(MissingInputCode.OptionAsk);
        if (contract.OpenInterest is null) liquidityMissing.Add(MissingInputCode.OptionOpenInterest);
        var liquidityInputs = new[] { InputOrMissing("BID", contract.Bid), InputOrMissing("ASK", contract.Ask),
            InputOrMissing("OPEN_INTEREST", contract.OpenInterest), InputOrMissing("BID_ASK_SPREAD_PERCENT", metrics.BidAskSpreadPercent) };
        var liquidityGate = liquidityMissing.Count > 0
            ? UnavailableGate(GateCode.Liquidity, liquidityInputs, liquidityMissing)
            : Gate(GateCode.Liquidity, contract.Bid > 0 && contract.Ask > contract.Bid &&
                contract.OpenInterest >= configuration.MinimumOpenInterest &&
                metrics.BidAskSpreadPercent <= configuration.MaximumBidAskSpreadPercent,
                RejectionReasonCode.InsufficientLiquidity, liquidityInputs);

        var premiumGate = metrics.ReferencePremium is { } referencePremium
            ? Gate(GateCode.PremiumFloor, referencePremium >= holding.MinimumPremium, RejectionReasonCode.PremiumBelowMinimum,
                [Input("REFERENCE_PREMIUM", referencePremium), Input("MINIMUM_PREMIUM", holding.MinimumPremium)])
            : UnavailableGate(GateCode.PremiumFloor, [Invalid("REFERENCE_PREMIUM", contract.Bid)], [MissingInputCode.OptionBid]);

        var yieldMissing = new List<MissingInputCode>();
        if (contract.Bid is null) yieldMissing.Add(MissingInputCode.OptionBid);
        if (!priceValid) yieldMissing.Add(MissingInputCode.OptionUnderlyingPrice);
        if (dte <= 0) yieldMissing.Add(MissingInputCode.ContractDte);
        var yieldGate = metrics.AnnualizedPremiumYield is { } annualized && double.IsFinite(annualized)
            ? Gate(GateCode.AnnualizedYieldFloor, annualized >= holding.MinimumAnnualizedYield,
                RejectionReasonCode.AnnualizedYieldBelowMinimum,
                [Input("ANNUALIZED_PREMIUM_YIELD", annualized), Input("MINIMUM_ANNUALIZED_YIELD", holding.MinimumAnnualizedYield)])
            : UnavailableGate(GateCode.AnnualizedYieldFloor,
                [Invalid("ANNUALIZED_PREMIUM_YIELD", metrics.AnnualizedPremiumYield)], yieldMissing);
        return [dteGate, strikeGate, deltaGate, earningsGate, liquidityGate, premiumGate, yieldGate];
    }

    private static ScoreResult ContractScore(EvaluationContext context, OptionContractContext contract, ContractDerivedMetrics metrics)
    {
        var holding = context.Holding;
        var config = context.Configuration.ContractScore;
        var maximumDelta = EffectiveMaximumDelta(holding, context.Configuration.ContractEligibility);
        var preferredMaximum = Math.Min(holding.PreferredDeltaMaximum, maximumDelta);
        var delta = contract.Delta!.Value;
        var deltaPoints = delta < holding.PreferredDeltaMinimum ? config.Delta.BelowPreferredPoints
            : delta <= preferredMaximum ? config.Delta.WithinPreferredPoints : config.Delta.AbovePreferredPoints;
        var deltaComponent = Available(ScoreComponentCode.ContractDelta, "Delta", deltaPoints, config.DeltaMaximumScore,
            [Input("DELTA", delta), Input("PREFERRED_DELTA_MINIMUM", holding.PreferredDeltaMinimum),
                Input("EFFECTIVE_PREFERRED_DELTA_MAXIMUM", preferredMaximum)]);

        var otm = metrics.OtmPercent!.Value;
        var strikeComponent = Available(ScoreComponentCode.ContractStrikeSafety, "Strike Safety", Score(config.StrikeSafety, otm),
            config.StrikeSafetyMaximumScore, [Input("OTM_PERCENT", otm)]);

        var ratio = metrics.AnnualizedPremiumYield!.Value / holding.MinimumAnnualizedYield;
        var premiumComponent = Available(ScoreComponentCode.ContractPremiumEfficiency, "Premium Efficiency",
            Score(config.PremiumEfficiency, ratio), config.PremiumEfficiencyMaximumScore,
            [Input("PREMIUM_EFFICIENCY_RATIO", ratio), Input("ANNUALIZED_PREMIUM_YIELD", metrics.AnnualizedPremiumYield.Value)]);

        var dte = metrics.Dte!.Value;
        var dteComponent = Available(ScoreComponentCode.ContractDteEfficiency, "DTE Efficiency", Score(config.DteEfficiency, dte),
            config.DteEfficiencyMaximumScore, [Input("DTE", dte)]);

        var ivValid = contract.ImpliedVolatility is { } iv && double.IsFinite(iv) && iv > 0;
        var rv = context.Indicators.RealizedVolatility30;
        var rvValid = rv.Status == IndicatorValueStatus.Available && rv.Value is { } rv30 && double.IsFinite(rv30) && rv30 > 0;
        var volatilityMissing = new List<MissingInputCode>();
        if (!ivValid) volatilityMissing.Add(MissingInputCode.OptionImpliedVolatility);
        if (!rvValid) volatilityMissing.Add(MissingInputCode.RealizedVolatility30);
        var volatilityInputs = new[] { ivValid ? Input("CONTRACT_IV", contract.ImpliedVolatility!.Value) : Invalid("CONTRACT_IV", contract.ImpliedVolatility),
            rvValid ? Input("RV30", rv.Value!.Value) : Invalid("RV30", rv.Value) };
        var volatilityComponent = volatilityMissing.Count == 0
            ? Available(ScoreComponentCode.ContractVolatilityEdge, "IV/Volatility Edge",
                Score(config.VolatilityEdge, contract.ImpliedVolatility!.Value / rv.Value!.Value), config.VolatilityEdgeMaximumScore,
                [.. volatilityInputs, Input("CONTRACT_VOLATILITY_EDGE", contract.ImpliedVolatility.Value / rv.Value.Value)])
            : Unavailable(ScoreComponentCode.ContractVolatilityEdge, "IV/Volatility Edge", config.VolatilityEdgeMaximumScore,
                volatilityInputs, volatilityMissing);

        var spread = metrics.BidAskSpreadPercent!.Value;
        var openInterest = contract.OpenInterest!.Value;
        var liquidityComponent = Available(ScoreComponentCode.ContractLiquidity, "Liquidity",
            Score(config.Liquidity.SpreadPercent, spread) +
            Score(config.Liquidity.OpenInterest, openInterest > int.MaxValue ? int.MaxValue : (int)openInterest),
            config.LiquidityMaximumScore, [Input("BID_ASK_SPREAD_PERCENT", spread), Input("OPEN_INTEREST", openInterest)]);

        var thetaValid = contract.Theta is { } theta && double.IsFinite(theta) && theta < 0;
        var thetaRatio = thetaValid ? -contract.Theta!.Value / (double)metrics.ReferencePremium!.Value : (double?)null;
        var thetaComponent = thetaValid && thetaRatio is { } validRatio && double.IsFinite(validRatio)
            ? Available(ScoreComponentCode.ContractThetaEfficiency, "Theta Efficiency", Score(config.ThetaEfficiency, validRatio),
                config.ThetaEfficiencyMaximumScore, [Input("THETA", contract.Theta!.Value), Input("THETA_EFFICIENCY_RATIO", validRatio)])
            : Unavailable(ScoreComponentCode.ContractThetaEfficiency, "Theta Efficiency", config.ThetaEfficiencyMaximumScore,
                [Invalid("THETA", contract.Theta)], [MissingInputCode.OptionTheta]);

        ScoreComponentResult[] components = [deltaComponent, strikeComponent, premiumComponent, dteComponent,
            volatilityComponent, liquidityComponent, thetaComponent];
        var missing = components.SelectMany(x => x.MissingInputs).Distinct().ToArray();
        if (missing.Length > 0)
            return new ScoreResult(ScoreStatus.Unavailable, null, 100, null, holding.MinimumContractScore, null,
                components, missing, "Contract Score is unavailable because a required component is unavailable.");
        var total = components.Sum(x => x.Score!.Value);
        var classification = Classify(total, config.Classification);
        return new ScoreResult(ScoreStatus.Available, total, 100, classification, holding.MinimumContractScore,
            total >= holding.MinimumContractScore, components, [], $"Contract Score is {total.ToString(CultureInfo.InvariantCulture)} ({classification}).");
    }

    public static string Classify(double score, ContractScoreClassificationConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        configuration.Validate();
        if (!double.IsFinite(score) || score < 0 || score > 100)
            throw new ArgumentOutOfRangeException(nameof(score));
        return score < configuration.WeakMinimum ? "REJECT"
            : score < configuration.AcceptableMinimum ? "WEAK"
            : score < configuration.GoodMinimum ? "ACCEPTABLE"
            : score < configuration.ExcellentMinimum ? "GOOD" : "EXCELLENT";
    }

    private static double EffectiveMaximumDelta(HoldingContext holding, ContractEligibilityConfiguration config) =>
        Math.Min(Math.Min(config.GlobalMaximumInitialDelta, holding.MaximumInitialDelta),
            holding.TaxSensitivity == TaxSensitivity.High ? config.HighTaxMaximumDelta : double.PositiveInfinity);

    private static GateResult Gate(GateCode code, bool passed, RejectionReasonCode failure, IReadOnlyList<ScoreInput> inputs) =>
        new(code, passed ? GateStatus.Passed : GateStatus.Failed, passed ? null : failure, inputs, [],
            passed ? $"{code} passed." : $"{code} failed: {failure}.");

    private static GateResult UnavailableGate(GateCode code, IReadOnlyList<ScoreInput> inputs, IReadOnlyList<MissingInputCode> missing) =>
        new(code, GateStatus.Unavailable, RejectionReasonCode.InsufficientData, inputs, missing,
            $"{code} is unavailable because required input is missing or invalid.");

    private static ScoreComponentResult Available(ScoreComponentCode code, string name, double score, double maximum,
        IReadOnlyList<ScoreInput> inputs) => new(code, name, ScoreStatus.Available, score, maximum, inputs, [], $"{name} scored {score}.");

    private static ScoreComponentResult Unavailable(ScoreComponentCode code, string name, double maximum,
        IReadOnlyList<ScoreInput> inputs, IReadOnlyList<MissingInputCode> missing) =>
        new(code, name, ScoreStatus.Unavailable, null, maximum, inputs, missing, $"{name} is unavailable.");

    private static ScoreInput Input(string code, object value) => new(code, AvailabilityStatus.Available, Format(value));

    private static ScoreInput Invalid(string code, object? value) => new(code, AvailabilityStatus.Unavailable,
        value is null ? null : Format(value));

    private static string? Format(object value) => value switch
    {
        double number => number.ToString("G17", CultureInfo.InvariantCulture),
        DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture)
    };

    private static ScoreInput InputOrMissing(string code, object? value) => value is null ? Invalid(code, null) : Input(code, value);

    private static double Score(ContinuousScoreTable table, double value) => table.Bands.Single(band =>
        (band.Minimum is null || value > band.Minimum || value == band.Minimum && band.IncludesMinimum) &&
        (band.Maximum is null || value < band.Maximum || value == band.Maximum && band.IncludesMaximum)).Points;

    private static double Score(IntegerScoreTable table, int value) => table.Bands.Single(band =>
        value >= band.Minimum && (band.Maximum is null || value <= band.Maximum)).Points;
}
