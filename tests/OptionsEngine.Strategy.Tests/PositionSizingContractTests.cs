using System.Collections.Immutable;
using OptionsEngine.Strategy.PositionSizing;

namespace OptionsEngine.Strategy.Tests;

public sealed class PositionSizingContractTests
{
    [Fact]
    public void TopLevelStatusesAreDistinctAndStable()
    {
        Assert.Equal(
            [PositionSizingStatus.Available, PositionSizingStatus.InsufficientData, PositionSizingStatus.NotApplicable],
            Enum.GetValues<PositionSizingStatus>());
        Assert.NotEqual(PositionSizingStatus.Available, PositionSizingStatus.InsufficientData);
        Assert.NotEqual(PositionSizingStatus.InsufficientData, PositionSizingStatus.NotApplicable);
    }

    [Fact]
    public void ApprovedReasonCodesRoundTripDeterministically()
    {
        var expected = new[]
        {
            PositionSizingReasonCode.NoEntryCandidate,
            PositionSizingReasonCode.CcosSizingBelowMinimum,
            PositionSizingReasonCode.ContractQualityBelowSizingMinimum,
            PositionSizingReasonCode.TargetRoundsBelowOneContract,
            PositionSizingReasonCode.AssignmentSensitivityLimit,
            PositionSizingReasonCode.HoldingMaximumCoverageLimit,
            PositionSizingReasonCode.NoAvailableShares,
            PositionSizingReasonCode.ExistingCoverageAboveTarget,
            PositionSizingReasonCode.ExistingExposureExceedsPhysicalCapacity,
            PositionSizingReasonCode.DeltaExposureLimit,
            PositionSizingReasonCode.ExistingDerAtOrAboveMaximum,
            PositionSizingReasonCode.InconsistentZeroShareExposure,
            PositionSizingReasonCode.InsufficientData,
            PositionSizingReasonCode.UnsupportedAssetType
        };

        Assert.Equal(expected, Enum.GetValues<PositionSizingReasonCode>());
        foreach (var code in expected)
            Assert.Equal(code, Enum.Parse<PositionSizingReasonCode>(code.ToString()));
    }

    [Fact]
    public void MissingInputsAreMachineReadableAndSeparateFromNumericZero()
    {
        var result = new PositionSizingResult
        {
            Status = PositionSizingStatus.InsufficientData,
            ReasonCodes = [PositionSizingReasonCode.InsufficientData],
            MissingInputs = [PositionSizingMissingInputCode.PreferredContractDelta],
            SharesOwned = 0,
            LimitingFactors = [],
            Explanation = "The preferred contract Delta is unavailable."
        };

        Assert.Equal(0, result.SharesOwned);
        Assert.Null(result.Ccos);
        Assert.Null(result.PreferredContractScore);
        Assert.Null(result.AdditionalContracts);
        Assert.Contains(PositionSizingMissingInputCode.PreferredContractDelta, result.MissingInputs);
    }

    [Fact]
    public void ExistingExposureAndDeltaContractsPreserveSeparateIdentityAndObservationTime()
    {
        var expiration = new DateOnly(2026, 10, 23);
        var observedAt = new DateTimeOffset(2026, 9, 18, 16, 0, 0, TimeSpan.Zero);
        var exposure = new ExistingShortCallExposure(Guid.NewGuid(), "MSFT261023C00500000", 2, 500m, expiration);
        var delta = new ExistingShortCallDeltaObservation(exposure.OptionSymbol, .17, observedAt);

        Assert.Equal(2, exposure.Contracts);
        Assert.Equal(500m, exposure.Strike);
        Assert.Equal(expiration, exposure.Expiration);
        Assert.Equal(exposure.OptionSymbol, delta.OptionSymbol);
        Assert.Equal(.17, delta.Delta);
        Assert.Equal(observedAt, delta.ObservationTimestampUtc);
    }

    [Fact]
    public void PortfolioConcentrationStatesRemainDistinctWithoutFabricatedWeight()
    {
        var targetHoldingId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var asOfDate = new DateOnly(2026, 9, 17);
        var notApplicable = new PortfolioConcentrationContext(
            PortfolioConcentrationStatus.NotApplicable, targetHoldingId, accountId, asOfDate, []);
        var unavailable = notApplicable with { Status = PortfolioConcentrationStatus.InsufficientData };

        Assert.NotEqual(notApplicable.Status, unavailable.Status);
        Assert.Empty(notApplicable.Holdings);

        var result = new PositionSizingResult
        {
            Status = PositionSizingStatus.Available,
            ReasonCodes = [],
            MissingInputs = [],
            SharesOwned = 100,
            PortfolioConcentrationStatus = PortfolioConcentrationStatus.NotApplicable,
            ConcentrationModifier = 1,
            LimitingFactors = [],
            Explanation = "ETF concentration is not applicable."
        };
        Assert.Null(result.PortfolioWeight);
        Assert.Equal(1, result.ConcentrationModifier);
    }

    [Fact]
    public void PositionSizingVersionIsIndependentAndValueComparable()
    {
        Assert.Equal(new PositionSizingStrategyVersion("1.0.0"), new PositionSizingStrategyVersion("1.0.0"));
        Assert.Throws<ArgumentException>(() => new PositionSizingStrategyVersion(" "));
        Assert.NotEqual(typeof(PositionSizingStrategyVersion),
            typeof(OptionsEngine.Strategy.Indicators.IndicatorCalculationVersion));
    }

    [Fact]
    public void PositionSizingContractsHaveNoProviderPersistenceOrHttpAssemblyDependency()
    {
        var references = typeof(PositionSizingResult).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(name => name is not null)
            .ToImmutableHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("OptionsEngine.MarketData", references);
        Assert.DoesNotContain("OptionsEngine.Infrastructure", references);
        Assert.DoesNotContain("OptionsEngine.Application", references);
        Assert.DoesNotContain("OptionsEngine.Api", references);
        Assert.DoesNotContain("Microsoft.EntityFrameworkCore", references);
        Assert.DoesNotContain("Microsoft.AspNetCore", references);
    }
}
