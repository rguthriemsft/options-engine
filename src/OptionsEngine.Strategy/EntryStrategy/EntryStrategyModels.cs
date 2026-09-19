using OptionsEngine.Domain.Accounts;
using OptionsEngine.Strategy.Indicators;

namespace OptionsEngine.Strategy.EntryStrategy;

/// <summary>Identifies a Phase 4 strategy implementation independently of its numeric configuration.</summary>
public sealed record StrategyVersion
{
    public StrategyVersion(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A strategy version is required.", nameof(value));

        Value = value;
    }

    public string Value { get; }
}

public enum AvailabilityStatus
{
    Available,
    Unavailable,
    NotApplicable
}

public enum GateStatus
{
    Passed,
    Failed,
    Unavailable,
    NotApplicable
}

public enum ScoreStatus
{
    Available,
    Unavailable,
    NotApplicable
}

public enum CcosClassification
{
    NoTrade,
    Weak,
    Watch,
    SellCandidate,
    Strong,
    Exceptional
}

public enum OptionContractType
{
    Call,
    Put
}

// These values are stable contracts. Their names, rather than explanation text, are used by later strategy, persistence, and API layers.
public enum GateCode
{
    BreakoutVeto,
    Dte,
    StrikeOtM,
    MaximumDelta,
    Earnings,
    Liquidity,
    PremiumFloor,
    AnnualizedYieldFloor
}

public enum ScoreComponentCode
{
    CcosVolatility,
    CcosRsi,
    CcosBollinger,
    CcosTrendMomentum,
    CcosResistanceStructure,
    CcosMarketSectorRegime,
    ContractDelta,
    ContractStrikeSafety,
    ContractPremiumEfficiency,
    ContractDteEfficiency,
    ContractVolatilityEdge,
    ContractLiquidity,
    ContractThetaEfficiency
}

public enum RejectionReasonCode
{
    InsufficientData,
    BreakoutVeto,
    DteOutsideRange,
    StrikeNotOtm,
    DeltaExceedsMaximum,
    EarningsBeforeExpiration,
    InsufficientLiquidity,
    PremiumBelowMinimum,
    AnnualizedYieldBelowMinimum
}

public enum DispositionReasonCode
{
    EntryCandidate,
    CcosBelowMinimum,
    BreakoutVeto,
    InsufficientData,
    NoAcceptableContract
}

public enum MissingInputCode
{
    HoldingMaximumInitialDelta,
    HoldingPreferredDeltaMinimum,
    HoldingPreferredDeltaMaximum,
    HoldingMinimumCcos,
    HoldingMinimumContractScore,
    HoldingMinimumPremium,
    HoldingMinimumAnnualizedYield,
    Iv30,
    IvPercentile,
    RealizedVolatility30,
    Rsi14,
    BollingerPercentB,
    BollingerBandwidth,
    Sma20,
    Sma50,
    Sma200,
    MacdHistogram,
    ResistanceStatus,
    ResistanceDistancePercent,
    ResistanceTouchCount,
    ResistanceAgeTradingDays,
    MarketRegime,
    SectorRegime,
    OptionUnderlyingPrice,
    OptionBid,
    OptionAsk,
    OptionOpenInterest,
    OptionDelta,
    OptionImpliedVolatility,
    OptionTheta,
    EarningsDate,
    ContractDte
}

/// <summary>Immutable snapshot of holding-specific values consumed by Phase 4.</summary>
public sealed record HoldingContext(
    Guid HoldingId,
    string Symbol,
    AssetType AssetType,
    AssignmentSensitivity AssignmentSensitivity,
    TaxSensitivity TaxSensitivity,
    double MaximumInitialDelta,
    double PreferredDeltaMinimum,
    double PreferredDeltaMaximum,
    double MinimumCcos,
    double MinimumContractScore,
    decimal MinimumPremium,
    double MinimumAnnualizedYield);

/// <summary>Immutable Phase 3 facts consumed by Phase 4; unavailable values remain unavailable.</summary>
public sealed record IndicatorContext(
    string Symbol,
    DateOnly AsOfDate,
    IndicatorValue<double> Iv30,
    IndicatorValue<double> IvPercentile,
    IndicatorValue<double> RealizedVolatility30,
    IndicatorValue<double> Rsi14,
    IndicatorValue<double> BollingerPercentB,
    IndicatorValue<double> BollingerBandwidth,
    IndicatorValue<decimal> Sma20,
    IndicatorValue<decimal> Sma50,
    IndicatorValue<decimal> Sma200,
    IndicatorValue<double> MacdHistogram,
    IndicatorValue<decimal> ResistancePrice,
    IndicatorValue<double> DistanceToResistancePercent,
    IndicatorValue<int> ResistanceTouchCount,
    IndicatorValue<int> ResistanceAgeTradingDays,
    ResistanceUnavailableReason? ResistanceUnavailableReason,
    MarketRegime MarketRegime,
    MarketRegime SectorRegime,
    IndicatorCalculationVersion IndicatorCalculationVersion,
    ConfigurationVersion ConfigurationVersion,
    DateTimeOffset IndicatorCalculatedAtUtc)
{
    public IndicatorValue<double> IvRank { get; init; } = IndicatorValue<double>.InsufficientData();
}

public sealed record EarningsContext(AvailabilityStatus Status, DateOnly? NextEarningsDate);

/// <summary>
/// Provider-independent normalized option observation retained by later Phase 4 evaluations.
/// ObservationTimestampUtc is the timestamp of its selected complete chain observation.
/// </summary>
public sealed record OptionContractContext(
    string OptionSymbol,
    string UnderlyingSymbol,
    DateTimeOffset ObservationTimestampUtc,
    DateOnly Expiration,
    decimal Strike,
    OptionContractType OptionType,
    decimal? Bid,
    decimal? Ask,
    long? OpenInterest,
    double? ImpliedVolatility,
    double? Delta,
    double? Theta,
    decimal? UnderlyingPrice,
    decimal? Last,
    long? Volume,
    string Provider);

/// <summary>All resolved Phase 4 numeric configuration used by an evaluation.</summary>
public sealed record EntryStrategyConfiguration
{
    public required ConfigurationVersion Version { get; init; }
    public CcosConfiguration Ccos { get; init; } = new();
    public ContractScoreConfiguration ContractScore { get; init; } = new();
    public ContractEligibilityConfiguration ContractEligibility { get; init; } = new();

    public void Validate()
    {
        if (Version.Value < 1)
            throw new ArgumentOutOfRangeException(nameof(Version), "Configuration version must be positive.");
        ArgumentNullException.ThrowIfNull(Ccos);
        ArgumentNullException.ThrowIfNull(ContractScore);
        ArgumentNullException.ThrowIfNull(ContractEligibility);
        ContractEligibility.Validate();
        Ccos.Validate();
        ContractScore.Validate(ContractEligibility);
    }

    /// <summary>Validates holding settings whose effective limits depend on this resolved configuration.</summary>
    public void Validate(HoldingContext holding)
    {
        ArgumentNullException.ThrowIfNull(holding);
        Validate();
        RequireFiniteRatio(holding.MaximumInitialDelta, nameof(holding.MaximumInitialDelta));
        RequireFiniteRatio(holding.PreferredDeltaMinimum, nameof(holding.PreferredDeltaMinimum));
        RequireFiniteRatio(holding.PreferredDeltaMaximum, nameof(holding.PreferredDeltaMaximum));
        RequireFiniteRange(holding.MinimumCcos, 0, 100, nameof(holding.MinimumCcos));
        RequireFiniteRange(holding.MinimumContractScore, 0, 100, nameof(holding.MinimumContractScore));
        if (holding.MinimumPremium < 0) throw new ArgumentOutOfRangeException(nameof(holding.MinimumPremium), "Minimum premium cannot be negative.");
        if (!double.IsFinite(holding.MinimumAnnualizedYield) || holding.MinimumAnnualizedYield <= 0)
            throw new ArgumentOutOfRangeException(nameof(holding.MinimumAnnualizedYield), "Minimum annualized yield must be finite and greater than zero.");

        var effectiveMaximumDelta = Math.Min(ContractEligibility.GlobalMaximumInitialDelta, holding.MaximumInitialDelta);
        if (holding.TaxSensitivity == TaxSensitivity.High)
            effectiveMaximumDelta = Math.Min(effectiveMaximumDelta, ContractEligibility.HighTaxMaximumDelta);
        var effectivePreferredDeltaMaximum = Math.Min(holding.PreferredDeltaMaximum, effectiveMaximumDelta);
        if (holding.PreferredDeltaMinimum > effectivePreferredDeltaMaximum)
            throw new ArgumentException("Preferred delta minimum cannot exceed the effective preferred delta maximum.", nameof(holding));
    }

    private static void RequireFiniteRatio(double value, string name) => RequireFiniteRange(value, 0, 1, name);

    private static void RequireFiniteRange(double value, double minimum, double maximum, string name)
    {
        if (!double.IsFinite(value) || value < minimum || value > maximum)
            throw new ArgumentOutOfRangeException(name, $"{name} must be finite and between {minimum} and {maximum}.");
    }
}

public sealed record CcosConfiguration
{
    public double VolatilityMaximumScore { get; init; } = 25;
    public double RsiMaximumScore { get; init; } = 15;
    public double BollingerMaximumScore { get; init; } = 15;
    public double TrendMomentumMaximumScore { get; init; } = 15;
    public double ResistanceStructureMaximumScore { get; init; } = 15;
    public double MarketSectorRegimeMaximumScore { get; init; } = 15;
    public double DefaultMinimumCcos { get; init; } = 70;
    public CcosClassificationConfiguration Classification { get; init; } = new();
    public CcosVolatilityScoringConfiguration Volatility { get; init; } = new();
    public ContinuousScoreTable Rsi { get; init; } = new() { Bands =
    [
        new(null, false, 40, false, 0), new(40, true, 50, false, 2),
        new(50, true, 55, false, 5), new(55, true, 60, false, 8),
        new(60, true, 65, false, 11), new(65, true, 70, false, 15),
        new(70, true, 75, false, 13), new(75, true, 80, true, 9),
        new(80, false, null, false, 5)
    ] };
    public CcosBollingerScoringConfiguration Bollinger { get; init; } = new();
    public CcosTrendScoringConfiguration TrendMomentum { get; init; } = new();
    public CcosResistanceScoringConfiguration ResistanceStructure { get; init; } = new();
    public CcosRegimeScoringConfiguration MarketSectorRegime { get; init; } = new();
    public BreakoutVetoConfiguration BreakoutVeto { get; init; } = new();

    internal void Validate()
    {
        var weights = new[] { VolatilityMaximumScore, RsiMaximumScore, BollingerMaximumScore, TrendMomentumMaximumScore,
            ResistanceStructureMaximumScore, MarketSectorRegimeMaximumScore };
        if (weights.Any(x => !double.IsFinite(x) || x < 0) || Math.Abs(weights.Sum() - 100) > 0.0000001)
            throw new ArgumentOutOfRangeException(nameof(VolatilityMaximumScore), "CCOS component maximum scores must be finite, non-negative, and total 100.");
        if (!double.IsFinite(DefaultMinimumCcos) || DefaultMinimumCcos < 0 || DefaultMinimumCcos > 100)
            throw new ArgumentOutOfRangeException(nameof(DefaultMinimumCcos), "The default minimum CCOS must be between 0 and 100.");
        ArgumentNullException.ThrowIfNull(Classification);
        ArgumentNullException.ThrowIfNull(Volatility);
        ArgumentNullException.ThrowIfNull(Rsi);
        ArgumentNullException.ThrowIfNull(Bollinger);
        ArgumentNullException.ThrowIfNull(TrendMomentum);
        ArgumentNullException.ThrowIfNull(ResistanceStructure);
        ArgumentNullException.ThrowIfNull(MarketSectorRegime);
        ArgumentNullException.ThrowIfNull(BreakoutVeto);
        Classification.Validate();
        Volatility.Validate(VolatilityMaximumScore);
        Rsi.Validate(nameof(Rsi), 9, RsiMaximumScore);
        Bollinger.Validate(BollingerMaximumScore);
        TrendMomentum.Validate(TrendMomentumMaximumScore);
        ResistanceStructure.Validate(ResistanceStructureMaximumScore);
        MarketSectorRegime.Validate(MarketSectorRegimeMaximumScore);
        BreakoutVeto.Validate();
    }
}

public sealed record ContractScoreConfiguration
{
    public double DeltaMaximumScore { get; init; } = 25;
    public double StrikeSafetyMaximumScore { get; init; } = 20;
    public double PremiumEfficiencyMaximumScore { get; init; } = 20;
    public double DteEfficiencyMaximumScore { get; init; } = 10;
    public double VolatilityEdgeMaximumScore { get; init; } = 10;
    public double LiquidityMaximumScore { get; init; } = 10;
    public double ThetaEfficiencyMaximumScore { get; init; } = 5;
    public double DefaultMinimumContractScore { get; init; } = 80;
    public ContractScoreClassificationConfiguration Classification { get; init; } = new();
    public ContractDeltaScoringConfiguration Delta { get; init; } = new();
    public ContinuousScoreTable StrikeSafety { get; init; } = new() { Bands =
    [
        new(0, false, .01, false, 0), new(.01, true, .02, false, 4),
        new(.02, true, .03, false, 8), new(.03, true, .04, false, 12),
        new(.04, true, .06, false, 15), new(.06, true, .08, true, 18),
        new(.08, false, null, false, 20)
    ] };
    public ContinuousScoreTable PremiumEfficiency { get; init; } = new() { Bands =
    [
        new(1, true, 1.10, false, 0), new(1.10, true, 1.25, false, 5),
        new(1.25, true, 1.50, false, 10), new(1.50, true, 2, false, 15),
        new(2, true, null, false, 20)
    ] };
    public IntegerScoreTable DteEfficiency { get; init; } = new() { Bands =
    [
        new(14, 20, 5), new(21, 35, 10), new(36, 45, 8)
    ] };
    public ContinuousScoreTable VolatilityEdge { get; init; } = new() { Bands =
    [
        new(null, false, .90, false, 0), new(.90, true, 1, false, 2),
        new(1, true, 1.10, false, 4), new(1.10, true, 1.20, false, 6),
        new(1.20, true, 1.35, true, 8), new(1.35, false, null, false, 10)
    ] };
    public ContractLiquidityScoringConfiguration Liquidity { get; init; } = new();
    public ContinuousScoreTable ThetaEfficiency { get; init; } = new() { Bands =
    [
        new(null, false, .01, false, 0), new(.01, true, .02, false, 1),
        new(.02, true, .03, false, 2), new(.03, true, .04, false, 3),
        new(.04, true, .05, false, 4), new(.05, true, null, false, 5)
    ] };

    internal void Validate(ContractEligibilityConfiguration eligibility)
    {
        var weights = new[] { DeltaMaximumScore, StrikeSafetyMaximumScore, PremiumEfficiencyMaximumScore, DteEfficiencyMaximumScore,
            VolatilityEdgeMaximumScore, LiquidityMaximumScore, ThetaEfficiencyMaximumScore };
        if (weights.Any(x => !double.IsFinite(x) || x < 0) || Math.Abs(weights.Sum() - 100) > 0.0000001)
            throw new ArgumentOutOfRangeException(nameof(DeltaMaximumScore), "Contract Score component maximum scores must be finite, non-negative, and total 100.");
        if (!double.IsFinite(DefaultMinimumContractScore) || DefaultMinimumContractScore < 0 || DefaultMinimumContractScore > 100)
            throw new ArgumentOutOfRangeException(nameof(DefaultMinimumContractScore), "The default minimum Contract Score must be between 0 and 100.");
        ArgumentNullException.ThrowIfNull(Classification);
        ArgumentNullException.ThrowIfNull(Delta);
        ArgumentNullException.ThrowIfNull(StrikeSafety);
        ArgumentNullException.ThrowIfNull(PremiumEfficiency);
        ArgumentNullException.ThrowIfNull(DteEfficiency);
        ArgumentNullException.ThrowIfNull(VolatilityEdge);
        ArgumentNullException.ThrowIfNull(Liquidity);
        ArgumentNullException.ThrowIfNull(ThetaEfficiency);
        Classification.Validate();
        Delta.Validate(DeltaMaximumScore);
        StrikeSafety.Validate(nameof(StrikeSafety), 7, StrikeSafetyMaximumScore, 0);
        PremiumEfficiency.Validate(nameof(PremiumEfficiency), 5, PremiumEfficiencyMaximumScore, 1);
        DteEfficiency.Validate(nameof(DteEfficiency), 3, DteEfficiencyMaximumScore, eligibility.MinimumDte, eligibility.MaximumDte);
        VolatilityEdge.Validate(nameof(VolatilityEdge), 6, VolatilityEdgeMaximumScore);
        Liquidity.Validate(LiquidityMaximumScore, eligibility.MaximumBidAskSpreadPercent, eligibility.MinimumOpenInterest);
        ThetaEfficiency.Validate(nameof(ThetaEfficiency), 6, ThetaEfficiencyMaximumScore);
    }
}

public sealed record ContractEligibilityConfiguration
{
    public int MinimumDte { get; init; } = 14;
    public int MaximumDte { get; init; } = 45;
    public double GlobalMaximumInitialDelta { get; init; } = 0.25;
    public double HighTaxMaximumDelta { get; init; } = 0.20;
    public long MinimumOpenInterest { get; init; } = 100;
    public double MaximumBidAskSpreadPercent { get; init; } = 0.20;

    internal void Validate()
    {
        if (MinimumDte < 1 || MaximumDte < MinimumDte)
            throw new ArgumentOutOfRangeException(nameof(MaximumDte), "DTE limits must be positive and ordered.");
        RequireDelta(GlobalMaximumInitialDelta, nameof(GlobalMaximumInitialDelta));
        RequireDelta(HighTaxMaximumDelta, nameof(HighTaxMaximumDelta));
        if (MinimumOpenInterest < 0) throw new ArgumentOutOfRangeException(nameof(MinimumOpenInterest), "Minimum open interest cannot be negative.");
        if (!double.IsFinite(MaximumBidAskSpreadPercent) || MaximumBidAskSpreadPercent < 0)
            throw new ArgumentOutOfRangeException(nameof(MaximumBidAskSpreadPercent), "Maximum bid-ask spread percent must be finite and non-negative.");
    }

    private static void RequireDelta(double value, string name)
    {
        if (!double.IsFinite(value) || value < 0 || value > 1)
            throw new ArgumentOutOfRangeException(name, "Delta limits must be finite and between 0 and 1.");
    }
}

/// <summary>A machine-readable input retained with a component or gate explanation.</summary>
public sealed record ScoreInput(string Code, AvailabilityStatus Status, string? Value);

public sealed record ScoreComponentResult(
    ScoreComponentCode Code,
    string Name,
    ScoreStatus Status,
    double? Score,
    double MaximumScore,
    IReadOnlyList<ScoreInput> Inputs,
    IReadOnlyList<MissingInputCode> MissingInputs,
    string Explanation);

public sealed record GateResult(
    GateCode Code,
    GateStatus Status,
    RejectionReasonCode? ReasonCode,
    IReadOnlyList<ScoreInput> Inputs,
    IReadOnlyList<MissingInputCode> MissingInputs,
    string Explanation);

public sealed record ScoreResult(
    ScoreStatus Status,
    double? Score,
    double MaximumScore,
    string? Classification,
    double ConfiguredMinimum,
    bool? MeetsConfiguredMinimum,
    IReadOnlyList<ScoreComponentResult> Components,
    IReadOnlyList<MissingInputCode> MissingInputs,
    string Explanation);

/// <summary>Derived values are nullable so Phase 4C can preserve unavailable inputs without a numeric sentinel.</summary>
public sealed record ContractDerivedMetrics(
    int? Dte,
    decimal? ReferencePremium,
    decimal? Mid,
    double? BidAskSpreadPercent,
    decimal? StrikeDistance,
    double? OtmPercent,
    double? PremiumYield,
    double? DailyPremiumYield,
    double? AnnualizedPremiumYield);

public sealed record ContractEvaluation(
    OptionContractContext Contract,
    ContractDerivedMetrics DerivedMetrics,
    IReadOnlyList<GateResult> Gates,
    ScoreResult? ContractScore,
    bool HardGateEligible,
    bool EntryAcceptable,
    int? Rank,
    IReadOnlyList<MissingInputCode> MissingInputs,
    IReadOnlyList<string> Explanations);

/// <summary>Pure Phase 4C decision; Application supplies persistence identity and calculation time later.</summary>
public sealed record ContractStrategyResult(
    ScoreResult Ccos,
    GateResult BreakoutVeto,
    IReadOnlyList<ContractEvaluation> Contracts,
    bool EntryCandidateExists,
    string? PreferredInitialOptionSymbol,
    decimal? PreferredInitialStrike,
    DateOnly? PreferredInitialExpiration,
    decimal? PreferredInitialReferencePremium,
    DispositionReasonCode DispositionReason,
    IReadOnlyList<MissingInputCode> MissingInputs,
    IReadOnlyList<string> Explanations);

/// <summary>Immutable Phase 4 result shape. Application supplies the permanent ID and calculation time before persistence.</summary>
public sealed record EntryStrategyEvaluation(
    Guid EntryStrategyEvaluationId,
    DateTimeOffset CalculatedAtUtc,
    EvaluationContext Context,
    ScoreResult? Ccos,
    IReadOnlyList<GateResult> UnderlyingGates,
    IReadOnlyList<ContractEvaluation> Contracts,
    bool EntryCandidateExists,
    string? PreferredInitialOptionSymbol,
    decimal? PreferredInitialStrike,
    DateOnly? PreferredInitialExpiration,
    decimal? PreferredInitialReferencePremium,
    DispositionReasonCode DispositionReason,
    IReadOnlyList<MissingInputCode> MissingInputs,
    IReadOnlyList<string> Explanations);

public sealed record EvaluationContext(
    HoldingContext Holding,
    IndicatorContext Indicators,
    EarningsContext Earnings,
    DateOnly IndicatorAsOfDate,
    DateTimeOffset EvaluationTimestampUtc,
    EntryStrategyConfiguration Configuration,
    StrategyVersion StrategyVersion)
{
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Holding);
        ArgumentNullException.ThrowIfNull(Indicators);
        ArgumentNullException.ThrowIfNull(Earnings);
        ArgumentNullException.ThrowIfNull(Configuration);
        ArgumentNullException.ThrowIfNull(StrategyVersion);
        if (EvaluationTimestampUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Evaluation timestamp must be expressed in UTC.", nameof(EvaluationTimestampUtc));
        if (IndicatorAsOfDate != Indicators.AsOfDate)
            throw new ArgumentException("Indicator as-of date must match the supplied indicator context.", nameof(IndicatorAsOfDate));
        if (!string.Equals(Holding.Symbol, Indicators.Symbol, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Holding and indicator context symbols must match.", nameof(Indicators));
        if (Configuration.Version != Indicators.ConfigurationVersion)
            throw new ArgumentException("Evaluation configuration version must match the consumed indicator context.", nameof(Configuration));
        Configuration.Validate(Holding);
    }
}
