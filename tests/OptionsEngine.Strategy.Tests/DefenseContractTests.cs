using OptionsEngine.Strategy.Defense;
using OptionsEngine.Strategy.Indicators;

namespace OptionsEngine.Strategy.Tests;

public sealed class DefenseContractTests
{
    [Fact]
    public void ApprovedDefaultConfigurationStructureValidates()
    {
        new DefenseConfiguration { Version = new ConfigurationVersion(1) }.Validate();
        Roll().Validate();
    }

    [Fact]
    public void ProfitTakingThresholdsMustBeStrictlyOrdered()
    {
        var invalid = new DefenseConfiguration
        {
            Version = new ConfigurationVersion(1),
            ProfitTaking = new ProfitTakingConfiguration { MonitorRatio = .70, CloseRatio = .70, StrongCloseRatio = .80 }
        };
        Assert.Throws<ArgumentException>(invalid.Validate);
    }

    [Fact]
    public void ReplacementDteAndDeltaRangesMustBeOrderedAndNested()
    {
        Assert.Throws<ArgumentException>(() => (Roll() with { PreferredMaximumDte = 61 }).Validate());
        Assert.Throws<ArgumentException>(() => (Roll() with { PreferredMinimumDelta = .19, PreferredMaximumDelta = .18 }).Validate());
        Assert.Throws<ArgumentException>(() => (Roll() with { HighTaxMaximumDelta = .26 }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (Roll() with { NormalMaximumDelta = double.NaN }).Validate());
    }

    [Fact]
    public void FullRatiosMustBeFiniteAndStrictlyPositive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => (Roll() with { FullStrikeImprovementRatio = 0 }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (Roll() with { FullCreditEconomicsRatio = double.PositiveInfinity }).Validate());
    }

    [Fact]
    public void MaximumRollDebitMayBeZeroButNotNegative()
    {
        (Roll() with { MaximumRollDebitPerShare = 0 }).Validate();
        Assert.Throws<ArgumentOutOfRangeException>(() => (Roll() with { MaximumRollDebitPerShare = -.01m }).Validate());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void StrategyVersionIdentitiesMustBeNonBlank(string? value)
    {
        Assert.Throws<ArgumentException>(() => new DefenseStrategyVersion(value!));
        Assert.Throws<ArgumentException>(() => new RollStrategyVersion(value!));
    }

    [Fact]
    public void NullableOpeningPremiumRemainsDistinctFromKnownZero()
    {
        var missing = Position(null);
        var zero = Position(0);
        Assert.Null(missing.OpeningPremiumPerShare);
        Assert.Equal(0m, zero.OpeningPremiumPerShare);
    }

    [Fact]
    public void MalformedDeltaRemainsMalformedAndIsNotNormalized()
    {
        var observation = new DefenseOptionObservation("MSFT-C", DateTimeOffset.UnixEpoch, "Test", null, null,
            null, null, null, -.25, null, null, null, null);
        Assert.Equal(-.25, observation.Delta);
    }

    private static RollConfiguration Roll() => new()
    {
        Version = new ConfigurationVersion(1),
        MaximumRollDebitPerShare = .25m
    };

    private static OpenShortCallPositionSnapshot Position(decimal? premium) => new(42, Guid.NewGuid(), "MSFT-C", 1,
        100m, new DateOnly(2026, 10, 16), premium, null);
}
