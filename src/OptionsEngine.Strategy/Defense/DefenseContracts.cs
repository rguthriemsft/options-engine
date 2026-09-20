using System.Collections.Immutable;
using OptionsEngine.Strategy.EntryStrategy;
using OptionsEngine.Strategy.Indicators;

namespace OptionsEngine.Strategy.Defense;

public sealed record OpenShortCallPositionSnapshot(long OpenShortCallPositionId, Guid HoldingId, string OptionSymbol,
    int Contracts, decimal Strike, DateOnly Expiration, decimal? OpeningPremiumPerShare, DateTimeOffset? OpenedAtUtc);
public sealed record DefenseOptionObservation(string OptionSymbol, DateTimeOffset ObservationTimestampUtc, string Provider,
    decimal? Bid, decimal? Ask, decimal? Last, long? OpenInterest, double? ImpliedVolatility, double? Delta,
    double? Gamma, double? Theta, double? Vega, decimal? UnderlyingPrice);

public enum DefenseDisposition { Hold, Monitor, CloseForProfit, Roll, CloseWait, DefenseReview, InsufficientData }
public enum ProfitTakingSignal { None, Monitor, CloseCandidate, StrongCloseCandidate, InsufficientData }
public enum DrsClassification { Safe, Normal, Watch, Defend, HighRisk, Critical }
public enum HardTriggerCode { HighDelta, StrikeProximityWithDelta, InTheMoney, LowDteWithDelta, RapidDeltaIncrease }
public enum HardTriggerStatus { Triggered, NotTriggered, InsufficientData, NotApplicable }
public enum HardDefenseStatus { Clear, Triggered, PartiallyEvaluated }
public enum RollCandidateEvaluationState { Eligible, Rejected, InsufficientData }
public enum DefenseReasonCode { NoDefenseActivation, ProfitTaking, DrsActivation, HardTriggerActivation, NoEligibleRollCandidate, CurrentCcosBelowRollThreshold, InsufficientData }
public enum DefenseMissingInputCode { OpeningPremiumPerShare, CurrentAsk, CurrentDelta, PreviousDelta, UnderlyingPrice, CurrentCcos, EarningsDate, Configuration }
public enum RollReasonCode { DteOutsideRange, ExpirationNotImproved, DeltaNotReduced, DeltaExceedsMaximum, StrikeNotImproved, StrikeNotStrictlyOtm, InsufficientLiquidity, EarningsCrossing, DebitExceedsMaximum, ProjectedDrsNotImproved, ProjectedDrsExceedsMaximum, InsufficientData }
public enum RollMissingInputCode { CandidateBid, CandidateAsk, CandidateDelta, CandidateOpenInterest, ExistingAsk, ExistingDelta, UnderlyingPrice, EarningsDate, CurrentDrs, ProjectedDrs, Configuration }
public enum RqsComponentCode { DrsReduction, DeltaReduction, StrikeImprovement, RollEconomics, ReplacementLiquidity, TimeEfficiency }
public enum EvaluationValueStatus { Available, InsufficientData, NotApplicable }

public sealed record ExplanationComponent(string Code, EvaluationValueStatus Status, double? Value, double MaximumValue,
    ImmutableArray<string> MissingInputs, string Explanation);
public sealed record ProfitTakingResult(EvaluationValueStatus Status, ProfitTakingSignal Signal,
    decimal? GrossOpeningPremium, decimal? EstimatedCurrentBtcCost, decimal? GrossPremiumCaptured,
    double? GrossPremiumCapturedRatio, ImmutableArray<DefenseMissingInputCode> MissingInputs, string Explanation);
public sealed record DrsResult(EvaluationValueStatus Status, double? Score, DrsClassification? Classification,
    ImmutableArray<ExplanationComponent> Components, ImmutableArray<DefenseMissingInputCode> MissingInputs, string Explanation);
public sealed record HardTriggerResult(HardTriggerCode Code, HardTriggerStatus Status,
    ImmutableDictionary<string, double?> ObservedValues, ImmutableArray<DefenseMissingInputCode> MissingInputs, string Explanation);
public sealed record RqsComponentResult(RqsComponentCode Code, EvaluationValueStatus Status, double? Score,
    double MaximumScore, ImmutableArray<RollMissingInputCode> MissingInputs, string Explanation);
public sealed record RqsResult(EvaluationValueStatus Status, double? Score, ImmutableArray<RqsComponentResult> Components,
    ImmutableArray<RollMissingInputCode> MissingInputs, string Explanation);
public sealed record RollCandidateEvaluation(DefenseOptionObservation Candidate, RollCandidateEvaluationState State,
    ImmutableArray<RollReasonCode> ReasonCodes, ImmutableArray<RollMissingInputCode> MissingInputs,
    decimal? ExistingBtcPerShare, decimal? ReplacementStoPerShare, decimal? NetRollPerShare, decimal? NetRollTotal,
    double? ProjectedDrs, RqsResult? Rqs, int? Rank, ImmutableArray<string> Explanations);
public sealed record RollEvaluation(Guid RollEvaluationId, Guid DefenseEvaluationId,
    DateTimeOffset DefenseEvaluationTimestampUtc, DateTimeOffset CalculatedAtUtc,
    OpenShortCallPositionSnapshot CurrentPositionSnapshot, double? CurrentCcos, decimal? ExistingBtcPerShare,
    ImmutableArray<RollCandidateEvaluation> Candidates, string? PreferredOptionSymbol, decimal? PreferredStrike,
    DateOnly? PreferredExpiration, double? PreferredRqs, RollConfiguration ResolvedConfiguration,
    ConfigurationVersion ConfigurationVersion, RollStrategyVersion StrategyVersion,
    ImmutableArray<RollMissingInputCode> MissingInputs, ImmutableArray<string> Explanations);
public sealed record DefenseEvaluationInput(OpenShortCallPositionSnapshot Position,
    DefenseOptionObservation? CurrentOptionObservation, DefenseOptionObservation? PreviousDeltaObservation,
    double? CurrentCcos, EarningsContext Earnings, DateTimeOffset DefenseEvaluationTimestampUtc,
    DefenseConfiguration DefenseConfiguration, RollConfiguration RollConfiguration,
    ConfigurationVersion ConfigurationVersion, DefenseStrategyVersion DefenseStrategyVersion,
    RollStrategyVersion RollStrategyVersion);
public sealed record DefenseEvaluation(Guid DefenseEvaluationId, long OpenShortCallPositionId, Guid HoldingId,
    string OptionSymbol, DateTimeOffset DefenseEvaluationTimestampUtc, DateTimeOffset CalculatedAtUtc,
    DefenseEvaluationInput Input, ProfitTakingResult ProfitTaking, DrsResult Drs,
    ImmutableArray<HardTriggerResult> HardTriggers, HardDefenseStatus HardDefenseStatus, bool RollEngineRequired,
    Guid? RollEvaluationId, DefenseDisposition Disposition, ImmutableArray<DefenseReasonCode> ReasonCodes,
    ImmutableArray<DefenseMissingInputCode> MissingInputs, ImmutableArray<string> Explanations);
