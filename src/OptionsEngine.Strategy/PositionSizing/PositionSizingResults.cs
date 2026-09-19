using System.Collections.Immutable;
using OptionsEngine.Strategy.Indicators;

namespace OptionsEngine.Strategy.PositionSizing;

/// <summary>
/// Structured Position Sizing result contract. Nullable values remain unavailable rather than masquerading as zero.
/// Values owned by later Phase 5 packets remain null until those calculations are available.
/// </summary>
public sealed record PositionSizingResult
{
    public required PositionSizingStatus Status { get; init; }
    public required ImmutableArray<PositionSizingReasonCode> ReasonCodes { get; init; }
    public required ImmutableArray<PositionSizingMissingInputCode> MissingInputs { get; init; }

    public decimal? SharesOwned { get; init; }
    public int? PhysicalCapacityContracts { get; init; }

    public int? ExistingCoveredContracts { get; init; }
    public decimal? ExistingCoveredShares { get; init; }
    public decimal? AvailableShares { get; init; }
    public int? AvailableContracts { get; init; }

    public double? Ccos { get; init; }
    public double? CcosBaseCoverageRatio { get; init; }

    public double? AssignmentSensitivityModifier { get; init; }
    public double? AssignmentSensitivityMaximumRatio { get; init; }

    public double? PreferredContractScore { get; init; }
    public double? ContractQualityModifier { get; init; }

    public PortfolioConcentrationStatus? PortfolioConcentrationStatus { get; init; }
    public double? PortfolioWeight { get; init; }
    public double? ConcentrationModifier { get; init; }

    public double? RawCoverageRatio { get; init; }
    public double? DesiredCoverageRatio { get; init; }

    public int? DesiredTotalContracts { get; init; }
    public int? DesiredAdditionalContracts { get; init; }
    public int? PhysicalLimitedAdditionalContracts { get; init; }

    public double? ExistingDeltaShares { get; init; }
    public double? ExistingDer { get; init; }
    public double? MaximumDer { get; init; }
    public int? DerLimitedAdditionalContracts { get; init; }

    public int? AdditionalContracts { get; init; }
    public int? ResultingTotalContracts { get; init; }

    public required ImmutableArray<PositionSizingLimitingFactor> LimitingFactors { get; init; }
    public required string Explanation { get; init; }
}

/// <summary>
/// Future immutable Phase 5 evaluation envelope. This contract introduces no persistence implementation in Phase 5A.
/// </summary>
public sealed record PositionSizingEvaluation(
    Guid PositionSizingEvaluationId,
    Guid EntryStrategyEvaluationId,
    DateTimeOffset CalculatedAtUtc,
    DateTimeOffset SizingTimestampUtc,
    PositionSizingHoldingContext HoldingContext,
    ImmutableArray<ExistingShortCallExposure> ExistingShortCallExposure,
    ImmutableArray<ExistingShortCallDeltaObservation> ExistingShortCallDeltaObservations,
    PortfolioConcentrationContext PortfolioConcentrationContext,
    PositionSizingConfiguration ResolvedConfiguration,
    ConfigurationVersion ConfigurationVersion,
    PositionSizingStrategyVersion StrategyVersion,
    PositionSizingResult Result);
