using System.Collections.Immutable;
using OptionsEngine.Domain.Accounts;
using OptionsEngine.Strategy.EntryStrategy;
using OptionsEngine.Strategy.Indicators;
using OptionsEngine.Strategy.PositionSizing;

namespace OptionsEngine.Strategy.Tests;

public sealed class PositionSizingEngineTests
{
    private static readonly Guid HoldingId = Guid.Parse("CA9C8356-BB0E-4C95-86A6-B6088E057983");
    private static readonly Guid AccountId = Guid.Parse("36D61DE7-AE60-4BD8-A9FE-753B0FD838D6");
    private static readonly DateTimeOffset SizingTimestamp =
        new(2026, 9, 18, 16, 0, 0, TimeSpan.Zero);
    private static readonly PositionSizingEngine Engine = new();

    [Theory]
    [InlineData(0, 0)]
    [InlineData(99, 0)]
    [InlineData(100, 1)]
    [InlineData(199, 1)]
    [InlineData(200, 2)]
    [InlineData(250, 2)]
    [InlineData(250.75, 2)]
    public void PhysicalCapacityFloorsAuthoritativeHoldingShares(double shares, int expected)
    {
        var result = Engine.Evaluate(Input(shares: (decimal)shares));

        Assert.Equal(PositionSizingStatus.Available, result.Status);
        Assert.Equal((decimal)shares, result.SharesOwned);
        Assert.Equal(expected, result.PhysicalCapacityContracts);
        Assert.DoesNotContain(PositionSizingMissingInputCode.Shares, result.MissingInputs);
    }

    [Theory]
    [InlineData(69.9999, 0)]
    [InlineData(70, .20)]
    [InlineData(70.0001, .20)]
    [InlineData(74.9999, .20)]
    [InlineData(75, .30)]
    [InlineData(75.0001, .30)]
    [InlineData(79.9999, .30)]
    [InlineData(80, .40)]
    [InlineData(80.0001, .40)]
    [InlineData(84.9999, .40)]
    [InlineData(85, .50)]
    [InlineData(85.0001, .50)]
    [InlineData(89.9999, .50)]
    [InlineData(90, .60)]
    [InlineData(90.0001, .60)]
    [InlineData(94.9999, .60)]
    [InlineData(95, .70)]
    [InlineData(95.0001, .70)]
    [InlineData(100, .70)]
    public void CcosBoundarySelectsApprovedBaseCoverage(double ccos, double expected)
    {
        var result = Engine.Evaluate(Input(ccos: ccos));

        Assert.Equal(expected, result.CcosBaseCoverageRatio);
    }

    [Theory]
    [InlineData(79.9999, 0)]
    [InlineData(80, .90)]
    [InlineData(80.0001, .90)]
    [InlineData(84.9999, .90)]
    [InlineData(85, 1.00)]
    [InlineData(85.0001, 1.00)]
    [InlineData(89.9999, 1.00)]
    [InlineData(90, 1.10)]
    [InlineData(90.0001, 1.10)]
    [InlineData(94.9999, 1.10)]
    [InlineData(95, 1.15)]
    [InlineData(95.0001, 1.15)]
    [InlineData(100, 1.15)]
    public void ContractScoreBoundarySelectsApprovedQualityModifier(double score, double expected)
    {
        var result = Engine.Evaluate(Input(contractScore: score));

        Assert.Equal(expected, result.ContractQualityModifier);
    }

    [Theory]
    [InlineData(AssignmentSensitivity.Level1, 1.25, 1.00)]
    [InlineData(AssignmentSensitivity.Level2, 1.10, .90)]
    [InlineData(AssignmentSensitivity.Level3, 1.00, .80)]
    [InlineData(AssignmentSensitivity.Level4, .85, .70)]
    [InlineData(AssignmentSensitivity.Level5, .70, .50)]
    public void AssignmentSensitivityUsesConfiguredModifierAndMaximum(
        AssignmentSensitivity sensitivity,
        double modifier,
        double maximum)
    {
        var result = Engine.Evaluate(Input(shares: 1_000m, ccos: 100, contractScore: 100,
            sensitivity: sensitivity));

        Assert.Equal(modifier, result.AssignmentSensitivityModifier);
        Assert.Equal(maximum, result.AssignmentSensitivityMaximumRatio);
        Assert.Equal(Math.Min(.70 * modifier * 1.15, maximum), result.DesiredCoverageRatio!.Value, 10);
    }

    [Fact]
    public void FormulaAppliesMultipliersBeforeBothTiedCapsAndReportsBothLimits()
    {
        var result = Engine.Evaluate(Input(
            shares: 1_000m,
            ccos: 100,
            contractScore: 100,
            sensitivity: AssignmentSensitivity.Level5,
            maximumCoverage: .50m,
            concentrationWeight: .049m));

        Assert.Equal(.70 * .70 * 1.15 * 1.10, result.RawCoverageRatio!.Value, 10);
        Assert.Equal(.049, result.PortfolioWeight!.Value, 10);
        Assert.Equal(1.10, result.ConcentrationModifier);
        Assert.Equal(.50, result.DesiredCoverageRatio);
        Assert.Equal(5, result.DesiredTotalContracts);
        Assert.Contains(PositionSizingReasonCode.AssignmentSensitivityLimit, result.ReasonCodes);
        Assert.Contains(PositionSizingReasonCode.HoldingMaximumCoverageLimit, result.ReasonCodes);
        Assert.Contains(result.LimitingFactors,
            factor => factor.Code == PositionSizingReasonCode.AssignmentSensitivityLimit);
        Assert.Contains(result.LimitingFactors,
            factor => factor.Code == PositionSizingReasonCode.HoldingMaximumCoverageLimit);
    }

    [Fact]
    public void RawCoverageIsLimitingWhenItIsBelowBothCaps()
    {
        var result = Engine.Evaluate(Input());

        Assert.Equal(.40, result.RawCoverageRatio);
        Assert.Equal(.40, result.DesiredCoverageRatio);
        Assert.Equal(.10, result.PortfolioWeight);
        Assert.Equal(1.00, result.ConcentrationModifier);
        Assert.Equal(0, result.ExistingCoveredContracts);
        Assert.DoesNotContain(PositionSizingReasonCode.AssignmentSensitivityLimit, result.ReasonCodes);
        Assert.DoesNotContain(PositionSizingReasonCode.HoldingMaximumCoverageLimit, result.ReasonCodes);
    }

    [Fact]
    public void StockConcentrationModifierParticipatesInExistingRawCoverageOrder()
    {
        var result = Engine.Evaluate(Input(concentrationWeight: .20m));

        Assert.Equal(.20, result.PortfolioWeight);
        Assert.Equal(.90, result.ConcentrationModifier);
        Assert.Equal(.40 * 1.00 * 1.00 * .90, result.RawCoverageRatio!.Value, 10);
    }

    [Fact]
    public void AssignmentSensitivityMaximumCanBeTheOnlyCoverageCap()
    {
        var result = Engine.Evaluate(Input(ccos: 100, contractScore: 100,
            sensitivity: AssignmentSensitivity.Level4, maximumCoverage: .90m,
            concentrationWeight: .049m));

        Assert.Equal(.70, result.DesiredCoverageRatio);
        Assert.Contains(PositionSizingReasonCode.AssignmentSensitivityLimit, result.ReasonCodes);
        Assert.DoesNotContain(PositionSizingReasonCode.HoldingMaximumCoverageLimit, result.ReasonCodes);
    }

    [Fact]
    public void HoldingMaximumCanBeTheOnlyCoverageCap()
    {
        var result = Engine.Evaluate(Input(ccos: 100, contractScore: 100,
            sensitivity: AssignmentSensitivity.Level2, maximumCoverage: .60m));

        Assert.Equal(.60, result.DesiredCoverageRatio);
        Assert.DoesNotContain(PositionSizingReasonCode.AssignmentSensitivityLimit, result.ReasonCodes);
        Assert.Contains(PositionSizingReasonCode.HoldingMaximumCoverageLimit, result.ReasonCodes);
    }

    [Fact]
    public void ZeroHoldingMaximumIsAnAvailableExplainedZero()
    {
        var result = Engine.Evaluate(Input(maximumCoverage: 0));

        Assert.Equal(PositionSizingStatus.Available, result.Status);
        Assert.Equal(0, result.DesiredCoverageRatio);
        Assert.Equal(0, result.AdditionalContracts);
        Assert.Contains(PositionSizingReasonCode.HoldingMaximumCoverageLimit, result.ReasonCodes);
    }

    [Fact]
    public void ZeroAssignmentSensitivityMaximumIsAnAvailableExplainedZero()
    {
        var defaults = DefaultConfiguration();
        var level3 = defaults.AssignmentSensitivitySettings[AssignmentSensitivity.Level3];
        var configuration = defaults with
        {
            AssignmentSensitivitySettings = defaults.AssignmentSensitivitySettings.SetItem(
                AssignmentSensitivity.Level3, level3 with { MaximumCoverageRatio = 0 })
        };

        var result = Engine.Evaluate(Input(configuration: configuration));

        Assert.Equal(PositionSizingStatus.Available, result.Status);
        Assert.Equal(0, result.DesiredCoverageRatio);
        Assert.Equal(0, result.AdditionalContracts);
        Assert.Contains(PositionSizingReasonCode.AssignmentSensitivityLimit, result.ReasonCodes);
    }

    [Theory]
    [InlineData(199.999, 0)]
    [InlineData(200, 1)]
    [InlineData(200.001, 1)]
    public void WholeContractTargetAlwaysFloors(double shares, int expected)
    {
        var configuration = ConfigurationWithConstantCoverage(.50);
        var result = Engine.Evaluate(Input(shares: (decimal)shares, contractScore: 85,
            configuration: configuration));

        Assert.Equal(expected, result.DesiredTotalContracts);
    }

    [Fact]
    public void ExistingExposureIsSubtractedBeforeAdditionalAction()
    {
        var result = Engine.Evaluate(Input(shares: 1_000m, existingContracts: 2));

        Assert.Equal(2, result.ExistingCoveredContracts);
        Assert.Equal(200m, result.ExistingCoveredShares);
        Assert.Equal(800m, result.AvailableShares);
        Assert.Equal(8, result.AvailableContracts);
        Assert.Equal(4, result.DesiredTotalContracts);
        Assert.Equal(2, result.DesiredAdditionalContracts);
        Assert.Equal(2, result.PhysicalLimitedAdditionalContracts);
        Assert.Equal(2, result.AdditionalContracts);
        Assert.Equal(4, result.ResultingTotalContracts);
        Assert.Null(result.ExistingDer);
        Assert.Null(result.MaximumDer);
        Assert.Null(result.DerLimitedAdditionalContracts);
    }

    [Theory]
    [InlineData(4, false)]
    [InlineData(5, true)]
    public void ExistingExposureAtOrAboveTargetProducesNoCloseRecommendation(
        int existingContracts,
        bool isAboveTarget)
    {
        var result = Engine.Evaluate(Input(shares: 1_000m, existingContracts: existingContracts));

        Assert.Equal(0, result.DesiredAdditionalContracts);
        Assert.Equal(0, result.AdditionalContracts);
        Assert.Equal(existingContracts, result.ResultingTotalContracts);
        Assert.Equal(isAboveTarget,
            result.ReasonCodes.Contains(PositionSizingReasonCode.ExistingCoverageAboveTarget));
    }

    [Fact]
    public void ExposureAbovePhysicalCapacityProducesExplicitAvailableZeroOutcome()
    {
        var result = Engine.Evaluate(Input(shares: 100m, existingContracts: 2));

        Assert.Equal(PositionSizingStatus.Available, result.Status);
        Assert.Equal(0m, result.AvailableShares);
        Assert.Equal(0, result.AvailableContracts);
        Assert.Equal(0, result.AdditionalContracts);
        Assert.Equal(2, result.ResultingTotalContracts);
        Assert.Contains(PositionSizingReasonCode.ExistingExposureExceedsPhysicalCapacity, result.ReasonCodes);
        Assert.Contains(PositionSizingReasonCode.NoAvailableShares, result.ReasonCodes);
    }

    [Fact]
    public void ExistingExposureThatFullyConsumesSharesProducesNoAdditionalContracts()
    {
        var result = Engine.Evaluate(Input(shares: 400m, existingContracts: 4));

        Assert.Equal(0m, result.AvailableShares);
        Assert.Equal(0, result.AvailableContracts);
        Assert.Equal(0, result.AdditionalContracts);
        Assert.Contains(PositionSizingReasonCode.NoAvailableShares, result.ReasonCodes);
        Assert.DoesNotContain(PositionSizingReasonCode.ExistingExposureExceedsPhysicalCapacity, result.ReasonCodes);
    }

    [Fact]
    public void ZeroSharesWithExistingExposureIsReportedWithoutDivision()
    {
        var result = Engine.Evaluate(Input(shares: 0, existingContracts: 1));

        Assert.Equal(PositionSizingStatus.Available, result.Status);
        Assert.Equal(0, result.AdditionalContracts);
        Assert.Null(result.ExistingDer);
        Assert.Contains(PositionSizingReasonCode.InconsistentZeroShareExposure, result.ReasonCodes);
        Assert.Contains(PositionSizingReasonCode.ExistingExposureExceedsPhysicalCapacity, result.ReasonCodes);
    }

    [Fact]
    public void NoEntryCandidateIsNotApplicableWithoutRequiringSizingInputs()
    {
        var input = Input(entryCandidateExists: false, existingContracts: 2) with
        {
            Holding = null,
            Configuration = null,
            PortfolioConcentration = null
        };

        var result = Engine.Evaluate(input);

        Assert.Equal(PositionSizingStatus.NotApplicable, result.Status);
        Assert.Single(result.ReasonCodes);
        Assert.Equal(PositionSizingReasonCode.NoEntryCandidate, result.ReasonCodes[0]);
        Assert.Empty(result.MissingInputs);
        Assert.Equal(0, result.AdditionalContracts);
        Assert.Equal(2, result.ResultingTotalContracts);
    }

    [Fact]
    public void BelowMinimumCcosIsAnAvailableExplainedZero()
    {
        var result = Engine.Evaluate(Input(ccos: 69.9999));

        Assert.Equal(PositionSizingStatus.Available, result.Status);
        Assert.Equal(0, result.CcosBaseCoverageRatio);
        Assert.Equal(0, result.AdditionalContracts);
        Assert.Contains(PositionSizingReasonCode.CcosSizingBelowMinimum, result.ReasonCodes);
    }

    [Fact]
    public void BelowMinimumPreferredContractScoreIsAnAvailableExplainedZero()
    {
        var result = Engine.Evaluate(Input(contractScore: 79.9999));

        Assert.Equal(PositionSizingStatus.Available, result.Status);
        Assert.Equal(0, result.ContractQualityModifier);
        Assert.Equal(0, result.AdditionalContracts);
        Assert.Contains(PositionSizingReasonCode.ContractQualityBelowSizingMinimum, result.ReasonCodes);
    }

    [Fact]
    public void PreferredContractScoreIsConsumedWithoutSelectingHigherScoredAlternate()
    {
        var source = SourceEvaluation(90, 79, true, includeHigherScoredAlternate: true);
        var result = Engine.Evaluate(Input(source: source));

        Assert.Equal(79, result.PreferredContractScore);
        Assert.Equal(0, result.ContractQualityModifier);
        Assert.Equal(0, result.AdditionalContracts);
    }

    [Fact]
    public void TargetBelowOneWholeContractIsAnAvailableExplainedZero()
    {
        var result = Engine.Evaluate(Input(shares: 199m, ccos: 70, contractScore: 85));

        Assert.Equal(PositionSizingStatus.Available, result.Status);
        Assert.Equal(0, result.DesiredTotalContracts);
        Assert.Contains(PositionSizingReasonCode.TargetRoundsBelowOneContract, result.ReasonCodes);
    }

    [Fact]
    public void MissingHoldingPreservesUnavailableSharesAndReportsAllHoldingInputs()
    {
        var result = Engine.Evaluate(Input() with { Holding = null });

        Assert.Equal(PositionSizingStatus.InsufficientData, result.Status);
        Assert.Null(result.SharesOwned);
        Assert.Contains(PositionSizingMissingInputCode.Shares, result.MissingInputs);
        Assert.Contains(PositionSizingMissingInputCode.MaximumCoverageRatio, result.MissingInputs);
        Assert.Contains(PositionSizingMissingInputCode.MaximumDeltaExposureRatio, result.MissingInputs);
    }

    [Fact]
    public void MissingParticipatingPortfolioPriceMakesOverallSizingInsufficient()
    {
        var context = ConcentrationContext(1_000m, .10m);
        var missingPrice = context.Holdings[1] with { AsOfPrice = null };

        var result = Engine.Evaluate(Input() with
        {
            PortfolioConcentration = context with
            {
                Holdings = context.Holdings.SetItem(1, missingPrice)
            }
        });

        Assert.Equal(PositionSizingStatus.InsufficientData, result.Status);
        Assert.Equal(PortfolioConcentrationStatus.InsufficientData, result.PortfolioConcentrationStatus);
        Assert.Contains(PositionSizingMissingInputCode.PortfolioPrice, result.MissingInputs);
        Assert.Null(result.PortfolioWeight);
        Assert.Null(result.ConcentrationModifier);
        Assert.Null(result.AdditionalContracts);
    }

    [Fact]
    public void EtfSizingUsesNeutralNotApplicableConcentrationWithoutPrices()
    {
        var input = Input(assetType: AssetType.ExchangeTradedFund) with
        {
            PortfolioConcentration = new PortfolioConcentrationContext(
                HoldingId, AccountId, new DateOnly(2026, 9, 17), [])
        };

        var result = Engine.Evaluate(input);

        Assert.Equal(PositionSizingStatus.Available, result.Status);
        Assert.Equal(PortfolioConcentrationStatus.NotApplicable, result.PortfolioConcentrationStatus);
        Assert.Null(result.PortfolioWeight);
        Assert.Equal(1, result.ConcentrationModifier);
    }

    [Fact]
    public void OtherAssetTypeRemainsExplicitlyUnsupported()
    {
        var result = Engine.Evaluate(Input(assetType: AssetType.Other));

        Assert.Equal(PositionSizingStatus.InsufficientData, result.Status);
        Assert.Contains(PositionSizingReasonCode.UnsupportedAssetType, result.ReasonCodes);
        Assert.Equal(PortfolioConcentrationStatus.InsufficientData, result.PortfolioConcentrationStatus);
        Assert.Null(result.ConcentrationModifier);
        Assert.Null(result.AdditionalContracts);
    }

    public static TheoryData<PositionSizingInput, PositionSizingMissingInputCode> MissingRequiredInputs => new()
    {
        { Input() with { SourceEntryStrategyEvaluation = null }, PositionSizingMissingInputCode.SourceEntryStrategyEvaluation },
        { Input() with { Configuration = null }, PositionSizingMissingInputCode.Configuration },
        { Input() with { ExistingShortCallExposure = default }, PositionSizingMissingInputCode.ExistingShortCallExposure },
        { Input() with { PortfolioConcentration = null }, PositionSizingMissingInputCode.PortfolioConcentrationContext },
        { Input(source: SourceEvaluation(null, 90, true)), PositionSizingMissingInputCode.Ccos },
        { Input(source: SourceEvaluation(90, 90, true, includePreferred: false)), PositionSizingMissingInputCode.PreferredContract },
        { Input(source: SourceEvaluation(90, null, true)), PositionSizingMissingInputCode.PreferredContractScore }
    };

    [Theory]
    [MemberData(nameof(MissingRequiredInputs))]
    public void MissingRequiredInputIsInsufficientDataWithoutNumericRepair(
        PositionSizingInput input,
        PositionSizingMissingInputCode expectedMissing)
    {
        var result = Engine.Evaluate(input);

        Assert.Equal(PositionSizingStatus.InsufficientData, result.Status);
        Assert.Contains(expectedMissing, result.MissingInputs);
        Assert.Contains(PositionSizingReasonCode.InsufficientData, result.ReasonCodes);
        Assert.Null(result.AdditionalContracts);
    }

    [Fact]
    public void InvalidHoldingRatiosAndNegativeSharesFailRatherThanClamp()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Engine.Evaluate(Input() with { Holding = Holding(-1m) }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Engine.Evaluate(Input() with { Holding = Holding(500m, maximumCoverage: 1.01m) }));
    }

    private static PositionSizingInput Input(
        decimal shares = 1_000m,
        double ccos = 80,
        double contractScore = 85,
        AssignmentSensitivity sensitivity = AssignmentSensitivity.Level3,
        decimal maximumCoverage = 1m,
        int existingContracts = 0,
        decimal concentrationWeight = .10m,
        bool entryCandidateExists = true,
        PositionSizingConfiguration? configuration = null,
        EntryStrategyEvaluation? source = null,
        AssetType assetType = AssetType.Stock) => new(
            source ?? SourceEvaluation(ccos, contractScore, entryCandidateExists),
            Holding(shares, sensitivity, maximumCoverage, assetType),
            existingContracts == 0
                ? []
                : [new ExistingShortCallExposure(HoldingId, "MSFT261023C00500000", existingContracts, 500m,
                    new DateOnly(2026, 10, 23))],
            [],
            ConcentrationContext(shares, concentrationWeight),
            SizingTimestamp,
            configuration ?? DefaultConfiguration(),
            new ConfigurationVersion(1),
            new PositionSizingStrategyVersion("1.0.0"));

    private static PortfolioConcentrationContext ConcentrationContext(
        decimal targetShares,
        decimal targetWeight)
    {
        var asOfDate = new DateOnly(2026, 9, 17);
        var otherMarketValue = targetShares == 0
            ? 1_000m
            : targetShares * (1 - targetWeight) / targetWeight;
        return new PortfolioConcentrationContext(HoldingId, AccountId, asOfDate,
        [
            new PortfolioConcentrationHolding(HoldingId, AccountId, "MSFT", targetShares, 1m, asOfDate),
            new PortfolioConcentrationHolding(
                Guid.Parse("C8A3CEBA-3E5C-4D06-B68D-27E28FD6840F"),
                AccountId,
                "OTHER",
                1m,
                otherMarketValue,
                asOfDate)
        ]);
    }

    private static PositionSizingHoldingContext Holding(
        decimal shares,
        AssignmentSensitivity sensitivity = AssignmentSensitivity.Level3,
        decimal maximumCoverage = 1m,
        AssetType assetType = AssetType.Stock) => new(
            HoldingId,
            AccountId,
            "MSFT",
            assetType,
            shares,
            sensitivity,
            TaxSensitivity.Moderate,
            maximumCoverage,
            .25);

    private static EntryStrategyEvaluation SourceEvaluation(
        double? ccos,
        double? preferredScore,
        bool entryCandidateExists,
        bool includePreferred = true,
        bool includeHigherScoredAlternate = false)
    {
        var preferred = Contract("PREFERRED", preferredScore, rank: 1);
        IReadOnlyList<ContractEvaluation> contracts = includePreferred
            ? includeHigherScoredAlternate
                ? [preferred, Contract("ALTERNATE", 100, rank: 2)]
                : [preferred]
            : [];

        return new EntryStrategyEvaluation(
            Guid.Parse("5289CB63-A5B7-4099-842C-1250E8872153"),
            SizingTimestamp.AddMinutes(-1),
            null!,
            Score(ccos),
            [],
            contracts,
            entryCandidateExists,
            entryCandidateExists ? "PREFERRED" : null,
            entryCandidateExists ? 500m : null,
            entryCandidateExists ? new DateOnly(2026, 10, 23) : null,
            entryCandidateExists ? 2m : null,
            entryCandidateExists ? DispositionReasonCode.EntryCandidate : DispositionReasonCode.NoAcceptableContract,
            [],
            []);
    }

    private static ContractEvaluation Contract(string optionSymbol, double? score, int rank)
    {
        var contract = new OptionContractContext(optionSymbol, "MSFT", SizingTimestamp.AddMinutes(-2),
            new DateOnly(2026, 10, 23), 500m, OptionContractType.Call, 1.9m, 2.1m, 500,
            .25, .30, -.04, 450m, 2m, 100, "TestProvider");
        return new ContractEvaluation(contract, new ContractDerivedMetrics(null, null, null, null, null, null,
            null, null, null), [], Score(score), true, true, rank, [], []);
    }

    private static ScoreResult? Score(double? score) => score is null
        ? null
        : new ScoreResult(ScoreStatus.Available, score, 100, null, 0, true, [], [], "Persisted score.");

    private static PositionSizingConfiguration DefaultConfiguration() => new()
    {
        Version = new ConfigurationVersion(1)
    };

    private static PositionSizingConfiguration ConfigurationWithConstantCoverage(double coverage) => new()
    {
        Version = new ConfigurationVersion(1),
        CcosBaseCoverage = new PositionSizingBandTable
        {
            Bands = [new PositionSizingBand(0, true, 100, true, coverage)]
        }
    };
}
