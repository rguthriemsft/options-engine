using System.Collections.Immutable;
using OptionsEngine.Strategy.EntryStrategy;
using OptionsEngine.Strategy.Indicators;
using OptionsEngine.Domain.Accounts;

namespace OptionsEngine.Strategy.Defense;

public sealed record OpenShortCallPositionSnapshot(long OpenShortCallPositionId, Guid HoldingId, string OptionSymbol,
    int Contracts, decimal Strike, DateOnly Expiration, decimal? OpeningPremiumPerShare, DateTimeOffset? OpenedAtUtc)
{
    public void Validate()
    {
        if (OpenShortCallPositionId <= 0) throw new ArgumentOutOfRangeException(nameof(OpenShortCallPositionId));
        if (HoldingId == Guid.Empty) throw new ArgumentException("A Holding ID is required.", nameof(HoldingId));
        ArgumentException.ThrowIfNullOrWhiteSpace(OptionSymbol);
        if (Contracts <= 0) throw new ArgumentOutOfRangeException(nameof(Contracts));
        if (Strike <= 0) throw new ArgumentOutOfRangeException(nameof(Strike));
        if (OpenedAtUtc is { } openedAt && openedAt.Offset != TimeSpan.Zero)
            throw new ArgumentException("OpenedAtUtc must be UTC.", nameof(OpenedAtUtc));
    }
}
public sealed record DefenseHoldingContext(Guid HoldingId, string Symbol, AssetType AssetType,
    AssignmentSensitivity AssignmentSensitivity, TaxSensitivity TaxSensitivity)
{
    public void Validate()
    {
        if (HoldingId == Guid.Empty) throw new ArgumentException("A Holding ID is required.", nameof(HoldingId));
        ArgumentException.ThrowIfNullOrWhiteSpace(Symbol);
        if (!Enum.IsDefined(AssetType)) throw new ArgumentOutOfRangeException(nameof(AssetType));
        if (!Enum.IsDefined(AssignmentSensitivity)) throw new ArgumentOutOfRangeException(nameof(AssignmentSensitivity));
        if (!Enum.IsDefined(TaxSensitivity)) throw new ArgumentOutOfRangeException(nameof(TaxSensitivity));
    }
}
public sealed record DefenseOptionObservation(string OptionSymbol, string UnderlyingSymbol,
    DateTimeOffset ObservationTimestampUtc, DateOnly Expiration, decimal Strike, OptionContractType OptionType,
    decimal? Bid, decimal? Ask, decimal? Last, long? OpenInterest, double? ImpliedVolatility, double? Delta,
    double? Gamma, double? Theta, double? Vega, decimal? UnderlyingPrice, string Provider)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(OptionSymbol);
        ArgumentException.ThrowIfNullOrWhiteSpace(UnderlyingSymbol);
        ArgumentException.ThrowIfNullOrWhiteSpace(Provider);
        if (ObservationTimestampUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("ObservationTimestampUtc must be UTC.", nameof(ObservationTimestampUtc));
        if (Strike <= 0) throw new ArgumentOutOfRangeException(nameof(Strike));
        if (!Enum.IsDefined(OptionType)) throw new ArgumentOutOfRangeException(nameof(OptionType));
    }
}

public enum DefenseDisposition { NoAction, Monitor, ProfitClose, DefenseReview, Roll, CloseWait }
public enum ProfitTakingSignal { None, Monitor, CloseCandidate, StrongCloseCandidate }
public enum DrsClassification { Safe, Normal, Watch, Defend, HighRisk, Critical }
public enum DrsComponentCode { Delta, StrikeProximity, Dte, PremiumExpansion }
public enum HardTriggerCode { HighDelta, StrikeProximityWithDelta, InTheMoney, LowDteWithDelta, RapidDeltaIncrease }
public enum HardTriggerStatus { Triggered, NotTriggered, InsufficientData, NotApplicable }
public enum HardDefenseStatus { Clear, Triggered, PartiallyEvaluated }
public enum RollCandidateEvaluationState { Rejected, Rankable, InsufficientData }
public enum ReplacementDteWindow { Preferred, Extended }
public enum DefenseReasonCode { NoDefenseActivation, ProfitTaking, DrsActivation, HardTriggerActivation, NoEligibleRollCandidate, CurrentCcosBelowRollThreshold, InsufficientData }
public enum DefenseMissingInputCode { OpeningPremiumPerShare, CurrentAsk, CurrentDelta, PreviousDelta, UnderlyingPrice, Dte, CurrentCcos, EarningsDate, Configuration }
public enum RollReasonCode { DteOutsideRange, ExpirationNotImproved, DeltaNotReduced, DeltaExceedsMaximum, StrikeNotImproved, StrikeNotStrictlyOtm, InsufficientLiquidity, EarningsCrossing, DebitExceedsMaximum, ProjectedDrsNotImproved, ProjectedDrsExceedsMaximum, InsufficientData, OptionTypeNotCall }
public enum RollMissingInputCode { CandidateBid, CandidateAsk, CandidateDelta, CandidateOpenInterest, ExistingAsk, ExistingDelta, UnderlyingPrice, EarningsDate, CurrentDrs, ProjectedDrs, Configuration, ExistingStrike, ReplacementStrike, NetRollPerShare, ReplacementStoPerShare, NewDte }
public enum RqsComponentCode { DrsReduction, DeltaReduction, StrikeImprovement, RollEconomics, ReplacementLiquidity, TimeEfficiency }
public enum EvaluationValueStatus { Available, InsufficientData, NotApplicable }

public sealed record ExplanationComponent(DrsComponentCode Code, EvaluationValueStatus Status, double? ObservedValue,
    double? Score, double MaximumScore, ImmutableArray<DefenseMissingInputCode> MissingInputs, string Explanation);
public sealed record ProfitTakingResult(EvaluationValueStatus Status, ProfitTakingSignal Signal,
    decimal? GrossOpeningPremium, decimal? EstimatedCurrentBtcCost, decimal? GrossPremiumCaptured,
    double? GrossPremiumCapturedRatio, ImmutableArray<DefenseMissingInputCode> MissingInputs, string Explanation);
public sealed record DrsResult(EvaluationValueStatus Status, double? Score, DrsClassification? Classification,
    ImmutableArray<ExplanationComponent> Components, ImmutableArray<DefenseMissingInputCode> MissingInputs, string Explanation);
public sealed record HardTriggerResult(HardTriggerCode Code, HardTriggerStatus Status,
    ImmutableDictionary<string, double?> ObservedValues, ImmutableArray<DefenseMissingInputCode> MissingInputs, string Explanation);
public sealed record DefenseStrategyResult(DateOnly DefenseEvaluationDate, int Dte,
    ProfitTakingResult ProfitTaking, DrsResult Drs, double? DeltaVelocity,
    ImmutableArray<HardTriggerResult> HardTriggers, HardDefenseStatus HardDefenseStatus, bool RollEngineRequired);
public sealed record RqsComponentResult(RqsComponentCode Code, EvaluationValueStatus Status, double? Score,
    double MaximumScore, ImmutableDictionary<string, double?> DerivedValues,
    ImmutableArray<RollMissingInputCode> MissingInputs, string Explanation);
public sealed record RqsResult(EvaluationValueStatus Status, double? Score, ImmutableArray<RqsComponentResult> Components,
    ImmutableArray<RollMissingInputCode> MissingInputs, string Explanation);
public sealed record RollQualityScoringInput(double? CurrentDrs, double? ProjectedDrs,
    double? CurrentDelta, double? NewDelta, decimal? ExistingStrike, decimal? ReplacementStrike,
    decimal? NetRollPerShare, decimal? RollDebitPerShare, decimal? ReplacementStoPerShare,
    decimal? CandidateBid, decimal? CandidateAsk, long? CandidateOpenInterest, int? NewDte,
    RollConfiguration Configuration);
public sealed record RollCandidateDerivedMetrics(int NewDte, int AdditionalDte,
    ReplacementDteWindow? DteWindow, bool? PreferredDeltaWindow,
    decimal StrikeImprovement, double StrikeImprovementRatio, double? DeltaReduction,
    double? BidAskSpreadPercent, int ReplacementContracts,
    decimal? ExistingBtcPerShare, decimal? ReplacementStoPerShare,
    decimal? NetRollPerShare, decimal? NetRollTotal, decimal? RollDebitPerShare,
    DrsResult ProjectedDrsResult, double? DrsReduction)
{
    public double? ProjectedDrs => ProjectedDrsResult.Score;
}
public sealed record RollCandidateEvaluation(DefenseOptionObservation Candidate, RollCandidateEvaluationState State,
    RollCandidateDerivedMetrics Metrics,
    ImmutableArray<RollReasonCode> ReasonCodes, ImmutableArray<RollMissingInputCode> MissingInputs,
    RqsResult? Rqs, int? Rank, ImmutableArray<string> Explanations);
public sealed record RollCandidateEvaluationInput(OpenShortCallPositionSnapshot Position,
    DefenseHoldingContext Holding, DefenseOptionObservation? CurrentOptionObservation,
    DefenseStrategyResult CurrentDefense, EarningsContext Earnings,
    DateTimeOffset DefenseEvaluationTimestampUtc,
    ImmutableArray<SelectedRollChainSnapshot> SelectedChainSnapshots,
    DrsConfiguration DrsConfiguration, RollConfiguration RollConfiguration)
{
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Position);
        ArgumentNullException.ThrowIfNull(Holding);
        ArgumentNullException.ThrowIfNull(CurrentDefense);
        ArgumentNullException.ThrowIfNull(Earnings);
        ArgumentNullException.ThrowIfNull(DrsConfiguration);
        ArgumentNullException.ThrowIfNull(RollConfiguration);
        Position.Validate();
        Holding.Validate();
        DrsConfiguration.Validate();
        RollConfiguration.Validate();
        if (Position.HoldingId != Holding.HoldingId)
            throw new ArgumentException("Position and Holding IDs must match.");
        if (DefenseEvaluationTimestampUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Defense evaluation timestamp must be UTC.",
                nameof(DefenseEvaluationTimestampUtc));
        if (CurrentOptionObservation is { } currentObservation)
            DefenseObservationValidation.ValidatePositionObservation(Position, Holding, currentObservation,
                DefenseEvaluationTimestampUtc, nameof(CurrentOptionObservation));
        var evaluationDate = DefenseEvaluator.EvaluationDate(DefenseEvaluationTimestampUtc);
        var dte = Position.Expiration.DayNumber - evaluationDate.DayNumber;
        if (CurrentDefense.DefenseEvaluationDate != evaluationDate || CurrentDefense.Dte != dte)
            throw new ArgumentException("Current defense result must use the same position and evaluation timestamp.",
                nameof(CurrentDefense));
        if (!CurrentDefense.RollEngineRequired)
            throw new ArgumentException("Roll candidate evaluation requires an activated Roll Engine.",
                nameof(CurrentDefense));
        if (SelectedChainSnapshots.IsDefault)
            throw new ArgumentException("Selected chain snapshots must be initialized.",
                nameof(SelectedChainSnapshots));
        foreach (var chain in SelectedChainSnapshots)
        {
            ArgumentNullException.ThrowIfNull(chain);
            chain.Validate();
            if (!string.Equals(chain.UnderlyingSymbol, Holding.Symbol, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Every selected chain must belong to the target Holding.",
                    nameof(SelectedChainSnapshots));
            if (chain.ObservationTimestampUtc > DefenseEvaluationTimestampUtc)
                throw new ArgumentException("Selected chain observations cannot be later than the evaluation cutoff.",
                    nameof(SelectedChainSnapshots));
        }
    }
}
public sealed record RollCandidateStrategyResult(
    ImmutableArray<SelectedRollChainSnapshot> SelectedChainSnapshots,
    ImmutableArray<RollCandidateEvaluation> Candidates,
    string? PreferredOptionSymbol, decimal? PreferredStrike,
    DateOnly? PreferredExpiration, double? PreferredRqs);
public sealed record DefenseDispositionEvaluationInput(DefenseStrategyResult CurrentDefense,
    RollCandidateStrategyResult? RollCandidates, double? CurrentCcos,
    RollConfiguration RollConfiguration);
public sealed record DefenseDispositionResult(DefenseDisposition Disposition,
    ImmutableArray<DefenseReasonCode> ReasonCodes,
    ImmutableArray<DefenseMissingInputCode> MissingInputs,
    ImmutableArray<string> Explanations);
public sealed record RollEvaluation(Guid RollEvaluationId, Guid DefenseEvaluationId,
    DateTimeOffset DefenseEvaluationTimestampUtc, DateTimeOffset CalculatedAtUtc,
    OpenShortCallPositionSnapshot CurrentPositionSnapshot, double? CurrentCcos, decimal? ExistingBtcPerShare,
    ImmutableArray<SelectedRollChainSnapshot> SelectedChainSnapshots,
    ImmutableArray<RollCandidateEvaluation> Candidates, string? PreferredOptionSymbol, decimal? PreferredStrike,
    DateOnly? PreferredExpiration, double? PreferredRqs, RollConfiguration ResolvedConfiguration,
    ConfigurationVersion ConfigurationVersion, RollStrategyVersion StrategyVersion,
    ImmutableArray<RollMissingInputCode> MissingInputs, ImmutableArray<string> Explanations);
public sealed record SelectedRollChainSnapshot(string UnderlyingSymbol, DateOnly Expiration,
    DateTimeOffset ObservationTimestampUtc, string Provider, ImmutableArray<DefenseOptionObservation> Contracts)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(UnderlyingSymbol);
        ArgumentException.ThrowIfNullOrWhiteSpace(Provider);
        if (ObservationTimestampUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("ObservationTimestampUtc must be UTC.", nameof(ObservationTimestampUtc));
        if (Contracts.IsDefault)
            throw new ArgumentException("Contracts must be initialized; an empty selected chain is valid.", nameof(Contracts));

        foreach (var contract in Contracts)
        {
            ArgumentNullException.ThrowIfNull(contract);
            contract.Validate();
            if (!string.Equals(contract.UnderlyingSymbol, UnderlyingSymbol, StringComparison.Ordinal) ||
                contract.Expiration != Expiration ||
                contract.ObservationTimestampUtc != ObservationTimestampUtc ||
                !string.Equals(contract.Provider, Provider, StringComparison.Ordinal))
                throw new ArgumentException("Every contract must belong to the selected chain snapshot.", nameof(Contracts));
        }
    }
}
public sealed record DefenseEvaluationInput(OpenShortCallPositionSnapshot Position, DefenseHoldingContext Holding,
    DefenseOptionObservation? CurrentOptionObservation, DefenseOptionObservation? PreviousDeltaObservation,
    double? CurrentCcos, EarningsContext Earnings, DateTimeOffset DefenseEvaluationTimestampUtc,
    DefenseConfiguration DefenseConfiguration, RollConfiguration RollConfiguration,
    ConfigurationVersion ConfigurationVersion, DefenseStrategyVersion DefenseStrategyVersion,
    RollStrategyVersion RollStrategyVersion)
{
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Position); ArgumentNullException.ThrowIfNull(Holding);
        ArgumentNullException.ThrowIfNull(DefenseConfiguration); ArgumentNullException.ThrowIfNull(RollConfiguration);
        ArgumentNullException.ThrowIfNull(DefenseStrategyVersion); ArgumentNullException.ThrowIfNull(RollStrategyVersion);
        Position.Validate(); Holding.Validate();
        if (Position.HoldingId != Holding.HoldingId) throw new ArgumentException("Position and Holding IDs must match.");
        if (DefenseEvaluationTimestampUtc.Offset != TimeSpan.Zero) throw new ArgumentException("Defense evaluation timestamp must be UTC.", nameof(DefenseEvaluationTimestampUtc));
        if (CurrentOptionObservation is { } currentObservation)
            DefenseObservationValidation.ValidatePositionObservation(Position, Holding, currentObservation,
                DefenseEvaluationTimestampUtc, nameof(CurrentOptionObservation));
        if (PreviousDeltaObservation is { } previousObservation)
            DefenseObservationValidation.ValidatePositionObservation(Position, Holding, previousObservation,
                DefenseEvaluationTimestampUtc, nameof(PreviousDeltaObservation));
        if (CurrentOptionObservation is { } current && PreviousDeltaObservation is { } previous &&
            previous.ObservationTimestampUtc >= current.ObservationTimestampUtc)
            throw new ArgumentException(
                "PreviousDeltaObservation must be earlier than CurrentOptionObservation.",
                nameof(PreviousDeltaObservation));
        DefenseConfiguration.Validate(); RollConfiguration.Validate();
        if (DefenseConfiguration.Version != ConfigurationVersion || RollConfiguration.Version != ConfigurationVersion)
            throw new ArgumentException("Defense and Roll configuration versions must match the supplied configuration version.");
    }

}
internal static class DefenseObservationValidation
{
    public static void ValidatePositionObservation(OpenShortCallPositionSnapshot position,
        DefenseHoldingContext holding, DefenseOptionObservation observation,
        DateTimeOffset evaluationTimestampUtc, string parameterName)
    {
        observation.Validate();
        if (!string.Equals(observation.OptionSymbol, position.OptionSymbol, StringComparison.Ordinal) ||
            !string.Equals(observation.UnderlyingSymbol, holding.Symbol, StringComparison.OrdinalIgnoreCase) ||
            observation.Expiration != position.Expiration ||
            observation.Strike != position.Strike ||
            observation.OptionType != OptionContractType.Call)
            throw new ArgumentException(
                "The option observation must identify the current open short-call contract.", parameterName);
        if (observation.ObservationTimestampUtc > evaluationTimestampUtc)
            throw new ArgumentException(
                "The option observation cannot be later than the defense evaluation cutoff.", parameterName);
    }
}
public sealed record DefenseEvaluation(Guid DefenseEvaluationId, long OpenShortCallPositionId, Guid HoldingId,
    string OptionSymbol, DateTimeOffset DefenseEvaluationTimestampUtc, DateTimeOffset CalculatedAtUtc,
    DefenseHoldingContext HoldingContext, DefenseEvaluationInput Input, ProfitTakingResult ProfitTaking, DrsResult Drs,
    ImmutableArray<HardTriggerResult> HardTriggers, HardDefenseStatus HardDefenseStatus, bool RollEngineRequired,
    Guid? RollEvaluationId, DefenseDisposition Disposition, ImmutableArray<DefenseReasonCode> ReasonCodes,
    ImmutableArray<DefenseMissingInputCode> MissingInputs, ImmutableArray<string> Explanations);
