using System.Collections.Immutable;
using OptionsEngine.Domain.Accounts;
using OptionsEngine.Strategy.EntryStrategy;

namespace OptionsEngine.Strategy.PositionSizing;

public interface IPositionSizingEngine
{
    PositionSizingResult Evaluate(PositionSizingInput input);
}

/// <summary>Pure Position Sizing calculation through Phase 5C. DER remains owned by Phase 5D.</summary>
public sealed class PositionSizingEngine : IPositionSizingEngine
{
    private readonly PortfolioConcentrationCalculator _concentrationCalculator;

    public PositionSizingEngine()
        : this(new PortfolioConcentrationCalculator())
    {
    }

    public PositionSizingEngine(PortfolioConcentrationCalculator concentrationCalculator)
    {
        ArgumentNullException.ThrowIfNull(concentrationCalculator);
        _concentrationCalculator = concentrationCalculator;
    }

    public PositionSizingResult Evaluate(PositionSizingInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var source = input.SourceEntryStrategyEvaluation;
        if (source is null)
            return Insufficient([PositionSizingMissingInputCode.SourceEntryStrategyEvaluation]);

        var existingContracts = TryGetExistingContracts(input.ExistingShortCallExposure);
        if (!source.EntryCandidateExists)
        {
            return new PositionSizingResult
            {
                Status = PositionSizingStatus.NotApplicable,
                ReasonCodes = [PositionSizingReasonCode.NoEntryCandidate],
                MissingInputs = [],
                ExistingCoveredContracts = existingContracts,
                ExistingCoveredShares = existingContracts is null ? null : existingContracts * 100m,
                AdditionalContracts = 0,
                ResultingTotalContracts = existingContracts,
                LimitingFactors = [],
                Explanation = "The source Phase 4 evaluation has no entry candidate."
            };
        }

        var missingInputs = new List<PositionSizingMissingInputCode>();
        if (input.Holding is null)
        {
            AddMissing(missingInputs, PositionSizingMissingInputCode.Shares);
            AddMissing(missingInputs, PositionSizingMissingInputCode.MaximumCoverageRatio);
            AddMissing(missingInputs, PositionSizingMissingInputCode.MaximumDeltaExposureRatio);
        }

        if (input.Configuration is null)
            AddMissing(missingInputs, PositionSizingMissingInputCode.Configuration);
        if (input.ExistingShortCallExposure.IsDefault)
            AddMissing(missingInputs, PositionSizingMissingInputCode.ExistingShortCallExposure);
        if (input.PortfolioConcentration is null)
            AddMissing(missingInputs, PositionSizingMissingInputCode.PortfolioConcentrationContext);

        var ccos = AvailableScore(source.Ccos);
        if (ccos is null)
            AddMissing(missingInputs, PositionSizingMissingInputCode.Ccos);

        var preferredContract = FindPreferredContract(source);
        if (preferredContract is null)
            AddMissing(missingInputs, PositionSizingMissingInputCode.PreferredContract);
        var preferredContractScore = AvailableScore(preferredContract?.ContractScore);
        if (preferredContract is not null && preferredContractScore is null)
            AddMissing(missingInputs, PositionSizingMissingInputCode.PreferredContractScore);

        PortfolioConcentrationResult? concentration = null;
        if (input.Holding is not null && input.Configuration is not null && input.PortfolioConcentration is not null)
        {
            concentration = _concentrationCalculator.Calculate(
                input.PortfolioConcentration,
                input.Holding,
                input.Configuration);
            foreach (var missingInput in concentration.MissingInputs)
                AddMissing(missingInputs, missingInput);
        }

        var unsupportedAssetType = input.Holding?.AssetType == AssetType.Other;
        if (missingInputs.Count > 0 || unsupportedAssetType)
        {
            return Insufficient(
                [.. missingInputs],
                input.Holding?.SharesOwned,
                existingContracts,
                ccos,
                preferredContractScore,
                concentration,
                unsupportedAssetType
                    ? [PositionSizingReasonCode.UnsupportedAssetType, PositionSizingReasonCode.InsufficientData]
                    : [PositionSizingReasonCode.InsufficientData]);
        }

        var holding = input.Holding!;
        var configuration = input.Configuration!;
        configuration.Validate(holding);
        if (input.ConfigurationVersion != configuration.Version)
            throw new ArgumentException("The supplied configuration version must match the resolved configuration.", nameof(input));
        if (input.SizingTimestampUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("The sizing timestamp must be expressed in UTC.", nameof(input));
        ArgumentNullException.ThrowIfNull(input.StrategyVersion);

        ValidateExistingExposure(input.ExistingShortCallExposure, holding.HoldingId);

        var physicalCapacity = checked((int)decimal.Floor(holding.SharesOwned / 100m));
        var existingCoveredShares = checked(existingContracts!.Value * 100m);
        var availableShares = Math.Max(0m, holding.SharesOwned - existingCoveredShares);
        var availableContracts = checked((int)decimal.Floor(availableShares / 100m));

        var ccosBaseCoverage = configuration.CcosBaseCoverage.Resolve(ccos!.Value);
        var assignmentSetting = configuration.AssignmentSensitivitySettings[holding.AssignmentSensitivity];
        var contractQualityModifier = configuration.ContractQualityModifiers.Resolve(preferredContractScore!.Value);
        var rawCoverage = ccosBaseCoverage * assignmentSetting.Modifier * contractQualityModifier *
                          concentration!.ConcentrationModifier!.Value;
        var holdingMaximumCoverage = decimal.ToDouble(holding.MaximumCoverageRatio);
        var desiredCoverage = Math.Min(rawCoverage,
            Math.Min(assignmentSetting.MaximumCoverageRatio, holdingMaximumCoverage));
        var desiredTotal = checked((int)decimal.Floor(
            holding.SharesOwned * (decimal)desiredCoverage / 100m));
        var desiredAdditional = Math.Max(0, desiredTotal - existingContracts.Value);
        var physicalLimitedAdditional = Math.Min(desiredAdditional, availableContracts);

        var reasons = new List<PositionSizingReasonCode>();
        var limitingFactors = new List<PositionSizingLimitingFactor>();
        if (ccosBaseCoverage == 0)
            AddReason(reasons, PositionSizingReasonCode.CcosSizingBelowMinimum);
        if (contractQualityModifier == 0)
            AddReason(reasons, PositionSizingReasonCode.ContractQualityBelowSizingMinimum);
        if (desiredCoverage > 0 && desiredTotal == 0)
            AddReason(reasons, PositionSizingReasonCode.TargetRoundsBelowOneContract);
        if (assignmentSetting.MaximumCoverageRatio < rawCoverage &&
            assignmentSetting.MaximumCoverageRatio <= holdingMaximumCoverage)
            AddLimit(reasons, limitingFactors, PositionSizingReasonCode.AssignmentSensitivityLimit,
                "The Assignment Sensitivity maximum limits total coverage.");
        if (holdingMaximumCoverage < rawCoverage &&
            holdingMaximumCoverage <= assignmentSetting.MaximumCoverageRatio)
            AddLimit(reasons, limitingFactors, PositionSizingReasonCode.HoldingMaximumCoverageLimit,
                "The Holding maximum limits total coverage.");
        if (existingContracts.Value > physicalCapacity)
            AddLimit(reasons, limitingFactors, PositionSizingReasonCode.ExistingExposureExceedsPhysicalCapacity,
                "Existing short-call exposure exceeds the Holding's physical covered-call capacity.");
        if (holding.SharesOwned == 0 && existingContracts.Value > 0)
            AddReason(reasons, PositionSizingReasonCode.InconsistentZeroShareExposure);
        if (availableContracts == 0)
            AddLimit(reasons, limitingFactors, PositionSizingReasonCode.NoAvailableShares,
                "No whole physically available covered-call contract remains.");
        else if (physicalLimitedAdditional < desiredAdditional)
            AddLimit(reasons, limitingFactors, PositionSizingReasonCode.NoAvailableShares,
                "Available shares limit the additional covered-call contracts.");
        if (existingContracts.Value > desiredTotal)
            AddReason(reasons, PositionSizingReasonCode.ExistingCoverageAboveTarget);

        var explanation = Explain(reasons, existingContracts.Value, desiredTotal, physicalLimitedAdditional);
        return new PositionSizingResult
        {
            Status = PositionSizingStatus.Available,
            ReasonCodes = [.. reasons],
            MissingInputs = [],
            SharesOwned = holding.SharesOwned,
            PhysicalCapacityContracts = physicalCapacity,
            ExistingCoveredContracts = existingContracts,
            ExistingCoveredShares = existingCoveredShares,
            AvailableShares = availableShares,
            AvailableContracts = availableContracts,
            Ccos = ccos,
            CcosBaseCoverageRatio = ccosBaseCoverage,
            AssignmentSensitivityModifier = assignmentSetting.Modifier,
            AssignmentSensitivityMaximumRatio = assignmentSetting.MaximumCoverageRatio,
            PreferredContractScore = preferredContractScore,
            ContractQualityModifier = contractQualityModifier,
            PortfolioConcentrationStatus = concentration.Status,
            PortfolioWeight = concentration.PortfolioWeight,
            ConcentrationModifier = concentration.ConcentrationModifier,
            RawCoverageRatio = rawCoverage,
            DesiredCoverageRatio = desiredCoverage,
            DesiredTotalContracts = desiredTotal,
            DesiredAdditionalContracts = desiredAdditional,
            PhysicalLimitedAdditionalContracts = physicalLimitedAdditional,
            ExistingDer = null,
            MaximumDer = null,
            DerLimitedAdditionalContracts = null,
            AdditionalContracts = physicalLimitedAdditional,
            ResultingTotalContracts = existingContracts + physicalLimitedAdditional,
            LimitingFactors = [.. limitingFactors],
            Explanation = explanation
        };
    }

    private static PositionSizingResult Insufficient(
        ImmutableArray<PositionSizingMissingInputCode> missingInputs,
        decimal? sharesOwned = null,
        int? existingContracts = null,
        double? ccos = null,
        double? preferredContractScore = null,
        PortfolioConcentrationResult? concentration = null,
        ImmutableArray<PositionSizingReasonCode> reasonCodes = default) => new()
        {
            Status = PositionSizingStatus.InsufficientData,
            ReasonCodes = reasonCodes.IsDefault
                ? [PositionSizingReasonCode.InsufficientData]
                : reasonCodes,
            MissingInputs = missingInputs,
            SharesOwned = sharesOwned,
            ExistingCoveredContracts = existingContracts,
            ExistingCoveredShares = existingContracts is null ? null : existingContracts * 100m,
            Ccos = ccos,
            PreferredContractScore = preferredContractScore,
            PortfolioConcentrationStatus = concentration?.Status,
            PortfolioWeight = concentration?.PortfolioWeight,
            ConcentrationModifier = concentration?.ConcentrationModifier,
            LimitingFactors = [],
            Explanation = concentration?.Status == PortfolioConcentrationStatus.InsufficientData
                ? concentration.Explanation
                : $"Position sizing is unavailable because {missingInputs.Length} required input(s) are missing or invalid."
        };

    private static int? TryGetExistingContracts(ImmutableArray<ExistingShortCallExposure> exposures) =>
        exposures.IsDefault
            ? null
            : checked(exposures.Sum(exposure => exposure.Contracts >= 0
                ? exposure.Contracts
                : throw new ArgumentOutOfRangeException(nameof(exposures),
                    "Existing contract counts cannot be negative.")));

    private static double? AvailableScore(ScoreResult? score) =>
        score is { Status: ScoreStatus.Available, Score: >= 0 and <= 100 } && double.IsFinite(score.Score.Value)
            ? score.Score
            : null;

    private static ContractEvaluation? FindPreferredContract(EntryStrategyEvaluation source)
    {
        if (string.IsNullOrWhiteSpace(source.PreferredInitialOptionSymbol) || source.Contracts is null)
            return null;

        return source.Contracts.SingleOrDefault(contract =>
            string.Equals(contract.Contract.OptionSymbol, source.PreferredInitialOptionSymbol,
                StringComparison.Ordinal));
    }

    private static void ValidateExistingExposure(
        ImmutableArray<ExistingShortCallExposure> exposures,
        Guid holdingId)
    {
        foreach (var exposure in exposures)
        {
            if (exposure.HoldingId != holdingId)
                throw new ArgumentException("Every existing short-call exposure must belong to the target Holding.",
                    nameof(exposures));
            if (exposure.Contracts < 0)
                throw new ArgumentOutOfRangeException(nameof(exposures), "Existing contract counts cannot be negative.");
        }
    }

    private static void AddMissing(List<PositionSizingMissingInputCode> inputs, PositionSizingMissingInputCode code)
    {
        if (!inputs.Contains(code)) inputs.Add(code);
    }

    private static void AddReason(List<PositionSizingReasonCode> reasons, PositionSizingReasonCode code)
    {
        if (!reasons.Contains(code)) reasons.Add(code);
    }

    private static void AddLimit(
        List<PositionSizingReasonCode> reasons,
        List<PositionSizingLimitingFactor> factors,
        PositionSizingReasonCode code,
        string explanation)
    {
        AddReason(reasons, code);
        if (factors.All(factor => factor.Code != code))
            factors.Add(new PositionSizingLimitingFactor(code, explanation));
    }

    private static string Explain(
        IReadOnlyCollection<PositionSizingReasonCode> reasons,
        int existingContracts,
        int desiredTotal,
        int additionalContracts)
    {
        if (reasons.Count > 0)
            return $"Position sizing completed with limiting or explanatory reason(s): {string.Join(", ", reasons)}.";
        if (additionalContracts == 0 && existingContracts == desiredTotal)
            return "Existing covered-call exposure already meets the desired total.";
        return "Position sizing completed from the persisted Phase 4 candidate and the supplied Holding snapshot.";
    }
}
