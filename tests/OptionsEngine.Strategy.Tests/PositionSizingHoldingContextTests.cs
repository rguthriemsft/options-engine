using OptionsEngine.Domain.Accounts;
using OptionsEngine.Strategy.PositionSizing;

namespace OptionsEngine.Strategy.Tests;

public sealed class PositionSizingHoldingContextTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    public void InclusiveRatioBoundariesAreValid(double maximumCoverageRatio, double maximumDeltaExposureRatio)
    {
        var context = Valid() with
        {
            MaximumCoverageRatio = (decimal)maximumCoverageRatio,
            MaximumDeltaExposureRatio = maximumDeltaExposureRatio
        };

        context.Validate();
    }

    [Theory]
    [InlineData(-.000001)]
    [InlineData(1.000001)]
    public void MaximumCoverageRatioOutsideInclusiveRangeIsInvalid(double ratio)
    {
        var context = Valid() with { MaximumCoverageRatio = (decimal)ratio };

        Assert.Throws<ArgumentOutOfRangeException>(context.Validate);
    }

    [Theory]
    [InlineData(-.000001)]
    [InlineData(1.000001)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void MaximumDeltaExposureRatioMustBeFiniteAndWithinInclusiveRange(double ratio)
    {
        var context = Valid() with { MaximumDeltaExposureRatio = ratio };

        Assert.Throws<ArgumentOutOfRangeException>(context.Validate);
    }

    [Fact]
    public void NegativeSharesAreInvalid()
    {
        var context = Valid() with { SharesOwned = -.0001m };

        Assert.Throws<ArgumentOutOfRangeException>(context.Validate);
    }

    [Fact]
    public void FractionalPositiveSharesAreValid()
    {
        var context = Valid() with { SharesOwned = 99.75m };

        context.Validate();
        Assert.Equal(99.75m, context.SharesOwned);
    }

    [Fact]
    public void PositionSizingUsesItsOwnHoldingSnapshotWithoutBroadeningPhase4Context()
    {
        var phase4Properties = typeof(OptionsEngine.Strategy.EntryStrategy.HoldingContext)
            .GetProperties().Select(property => property.Name);
        var sizingProperties = typeof(PositionSizingHoldingContext)
            .GetProperties().Select(property => property.Name);

        Assert.DoesNotContain(nameof(PositionSizingHoldingContext.SharesOwned), phase4Properties);
        Assert.DoesNotContain(nameof(PositionSizingHoldingContext.MaximumCoverageRatio), phase4Properties);
        Assert.Contains(nameof(PositionSizingHoldingContext.SharesOwned), sizingProperties);
        Assert.Contains(nameof(PositionSizingHoldingContext.MaximumCoverageRatio), sizingProperties);
    }

    private static PositionSizingHoldingContext Valid() => new(
        Guid.Parse("1B539C36-CA1A-4FB0-B34A-84C821258AAB"),
        Guid.Parse("AB630E97-094B-44C4-8EBF-92340B52E251"),
        "MSFT",
        AssetType.Stock,
        250.5m,
        AssignmentSensitivity.Level3,
        TaxSensitivity.Moderate,
        .80m,
        .15);
}
