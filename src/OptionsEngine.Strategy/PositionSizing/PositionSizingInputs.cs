using System.Collections.Immutable;
using OptionsEngine.Domain.Accounts;
using OptionsEngine.Strategy.EntryStrategy;
using OptionsEngine.Strategy.Indicators;

namespace OptionsEngine.Strategy.PositionSizing;

/// <summary>Immutable snapshot of holding-specific values consumed only by Position Sizing.</summary>
public sealed record PositionSizingHoldingContext(
    Guid HoldingId,
    Guid AccountId,
    string Symbol,
    AssetType AssetType,
    decimal SharesOwned,
    AssignmentSensitivity AssignmentSensitivity,
    TaxSensitivity TaxSensitivity,
    decimal MaximumCoverageRatio,
    double MaximumDeltaExposureRatio)
{
    public void Validate()
    {
        if (HoldingId == Guid.Empty) throw new ArgumentException("A Holding ID is required.", nameof(HoldingId));
        if (AccountId == Guid.Empty) throw new ArgumentException("An Account ID is required.", nameof(AccountId));
        ArgumentException.ThrowIfNullOrWhiteSpace(Symbol);
        if (!Enum.IsDefined(AssetType)) throw new ArgumentOutOfRangeException(nameof(AssetType));
        if (!Enum.IsDefined(AssignmentSensitivity)) throw new ArgumentOutOfRangeException(nameof(AssignmentSensitivity));
        if (!Enum.IsDefined(TaxSensitivity)) throw new ArgumentOutOfRangeException(nameof(TaxSensitivity));
        if (SharesOwned < 0) throw new ArgumentOutOfRangeException(nameof(SharesOwned), "Shares owned cannot be negative.");
        if (MaximumCoverageRatio < 0 || MaximumCoverageRatio > 1)
            throw new ArgumentOutOfRangeException(nameof(MaximumCoverageRatio),
                "Maximum coverage ratio must be between 0 and 1.");
        if (!double.IsFinite(MaximumDeltaExposureRatio) ||
            MaximumDeltaExposureRatio < 0 || MaximumDeltaExposureRatio > 1)
            throw new ArgumentOutOfRangeException(nameof(MaximumDeltaExposureRatio),
                "Maximum Delta exposure ratio must be finite and between 0 and 1.");
    }
}

/// <summary>Net currently open short-call exposure for one option contract associated with a Holding.</summary>
public sealed record ExistingShortCallExposure(
    Guid HoldingId,
    string OptionSymbol,
    int Contracts,
    decimal Strike,
    DateOnly Expiration);

/// <summary>Normalized Delta observation selected for an existing short-call position.</summary>
public sealed record ExistingShortCallDeltaObservation(
    string OptionSymbol,
    double? Delta,
    DateTimeOffset ObservationTimestampUtc);

public enum PortfolioConcentrationStatus
{
    Available,
    InsufficientData,
    NotApplicable
}

/// <summary>A tracked same-account Holding and the persisted price selected for the concentration cutoff.</summary>
public sealed record PortfolioConcentrationHolding(
    Guid HoldingId,
    Guid AccountId,
    string Symbol,
    decimal SharesOwned,
    decimal? AsOfPrice,
    DateOnly? PriceObservationDate);

/// <summary>Provider-independent objective inputs for the pure portfolio concentration calculation.</summary>
public sealed record PortfolioConcentrationContext(
    Guid TargetHoldingId,
    Guid AccountId,
    DateOnly IndicatorAsOfDate,
    ImmutableArray<PortfolioConcentrationHolding> Holdings);

/// <summary>Complete immutable Strategy input boundary for the pure Position Sizing engine.</summary>
public sealed record PositionSizingInput(
    EntryStrategyEvaluation? SourceEntryStrategyEvaluation,
    PositionSizingHoldingContext? Holding,
    ImmutableArray<ExistingShortCallExposure> ExistingShortCallExposure,
    ImmutableArray<ExistingShortCallDeltaObservation> ExistingShortCallDeltaObservations,
    PortfolioConcentrationContext? PortfolioConcentration,
    DateTimeOffset SizingTimestampUtc,
    PositionSizingConfiguration? Configuration,
    ConfigurationVersion ConfigurationVersion,
    PositionSizingStrategyVersion StrategyVersion);
