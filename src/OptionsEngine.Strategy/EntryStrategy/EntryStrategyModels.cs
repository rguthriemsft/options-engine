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

public enum OptionContractType
{
    Call,
    Put
}

// These values are stable contracts. Their names, rather than explanation text, are used by later strategy, persistence, and API layers.
public enum GateCode
{
    CcosMinimum,
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
    CcosBelowMinimum,
    BreakoutVeto,
    DteOutsideRange,
    StrikeNotOtm,
    DeltaExceedsMaximum,
    EarningsBeforeExpiration,
    InsufficientLiquidity,
    PremiumBelowMinimum,
    AnnualizedYieldBelowMinimum,
    ContractScoreBelowMinimum
}

public enum DispositionReasonCode
{
    EntryCandidateSelected,
    CcosBelowMinimum,
    BreakoutVeto,
    InsufficientData,
    NoEligibleContracts,
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
    Resistance,
    MarketRegime,
    SectorRegime,
    OptionUnderlyingPrice,
    OptionBid,
    OptionAsk,
    OptionOpenInterest,
    OptionDelta,
    OptionImpliedVolatility,
    OptionTheta,
    EarningsDate
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
    ConfigurationVersion ConfigurationVersion)
{
    public IndicatorValue<double> IvRank { get; init; } = IndicatorValue<double>.InsufficientData();
}

public sealed record EarningsContext(AvailabilityStatus Status, DateOnly? NextEarningsDate);

/// <summary>
/// Provider-independent normalized option observation retained by later Phase 4 evaluations.
/// It intentionally contains no provider DTO or transport concern.
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
    decimal? UnderlyingPrice);

/// <summary>All resolved Phase 4 numeric configuration used by an evaluation.</summary>
public sealed record EntryStrategyConfiguration
{
    public required ConfigurationVersion Version { get; init; }
    public required DateOnly EffectiveDate { get; init; }
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
        Ccos.Validate();
        ContractScore.Validate();
        ContractEligibility.Validate();
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

    internal void Validate()
    {
        var weights = new[] { VolatilityMaximumScore, RsiMaximumScore, BollingerMaximumScore, TrendMomentumMaximumScore,
            ResistanceStructureMaximumScore, MarketSectorRegimeMaximumScore };
        if (weights.Any(x => !double.IsFinite(x) || x < 0) || Math.Abs(weights.Sum() - 100) > 0.0000001)
            throw new ArgumentOutOfRangeException(nameof(VolatilityMaximumScore), "CCOS component maximum scores must be finite, non-negative, and total 100.");
        if (!double.IsFinite(DefaultMinimumCcos) || DefaultMinimumCcos < 0 || DefaultMinimumCcos > 100)
            throw new ArgumentOutOfRangeException(nameof(DefaultMinimumCcos), "The default minimum CCOS must be between 0 and 100.");
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

    internal void Validate()
    {
        var weights = new[] { DeltaMaximumScore, StrikeSafetyMaximumScore, PremiumEfficiencyMaximumScore, DteEfficiencyMaximumScore,
            VolatilityEdgeMaximumScore, LiquidityMaximumScore, ThetaEfficiencyMaximumScore };
        if (weights.Any(x => !double.IsFinite(x) || x < 0) || Math.Abs(weights.Sum() - 100) > 0.0000001)
            throw new ArgumentOutOfRangeException(nameof(DeltaMaximumScore), "Contract Score component maximum scores must be finite, non-negative, and total 100.");
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
    int? Rank,
    IReadOnlyList<MissingInputCode> MissingInputs,
    IReadOnlyList<string> Explanations);

/// <summary>Immutable, provider-independent Phase 4 result shape. Persistence is deliberately deferred to Phase 4E.</summary>
public sealed record EntryStrategyEvaluation(
    Guid EntryStrategyEvaluationId,
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
