namespace OptionsEngine.Strategy.PositionSizing;

/// <summary>Identifies Position Sizing algorithmic behavior independently of configuration values.</summary>
public sealed record PositionSizingStrategyVersion
{
    public PositionSizingStrategyVersion(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A Position Sizing strategy version is required.", nameof(value));

        Value = value;
    }

    public string Value { get; }
}

public enum PositionSizingStatus
{
    Available,
    InsufficientData,
    NotApplicable
}

// Stable machine-readable contracts. Human-readable explanations must not be parsed as strategy behavior.
public enum PositionSizingReasonCode
{
    NoEntryCandidate,
    CcosSizingBelowMinimum,
    ContractQualityBelowSizingMinimum,
    TargetRoundsBelowOneContract,
    AssignmentSensitivityLimit,
    HoldingMaximumCoverageLimit,
    NoAvailableShares,
    ExistingCoverageAboveTarget,
    ExistingExposureExceedsPhysicalCapacity,
    DeltaExposureLimit,
    ExistingDerAtOrAboveMaximum,
    InconsistentZeroShareExposure,
    InsufficientData,
    UnsupportedAssetType
}

public enum PositionSizingMissingInputCode
{
    Shares,
    MaximumCoverageRatio,
    MaximumDeltaExposureRatio,
    SourceEntryStrategyEvaluation,
    Ccos,
    PreferredContract,
    PreferredContractScore,
    PreferredContractDelta,
    ExistingShortCallDelta,
    PortfolioPrice,
    PortfolioDenominator,
    Configuration,
    ExistingShortCallExposure,
    PortfolioConcentrationContext,
    PortfolioTargetHolding
}

public sealed record PositionSizingLimitingFactor(
    PositionSizingReasonCode Code,
    string Explanation);
