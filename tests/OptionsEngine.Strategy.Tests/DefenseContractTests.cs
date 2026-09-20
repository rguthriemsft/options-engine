using OptionsEngine.Strategy.Defense;
using OptionsEngine.Strategy.Indicators;
using OptionsEngine.Strategy.EntryStrategy;
using OptionsEngine.Domain.Accounts;

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

    [Fact]
    public void DefenseInputValidatesIdentityHoldingTimeAndSharedConfigurationVersion()
    {
        var input = Input();
        input.Validate();
        Assert.Throws<ArgumentOutOfRangeException>(() => (input with { Position = Position(null) with { OpenShortCallPositionId = 0 } }).Validate());
        Assert.Throws<ArgumentException>(() => (input with { Holding = input.Holding with { HoldingId = Guid.NewGuid() } }).Validate());
        Assert.Throws<ArgumentException>(() => (input with
        {
            DefenseEvaluationTimestampUtc = new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.FromHours(-7))
        }).Validate());
        Assert.Throws<ArgumentException>(() => (input with { ConfigurationVersion = new ConfigurationVersion(2) }).Validate());
    }

    [Fact]
    public void EmptySelectedRollChainRemainsRepresentable()
    {
        var chain = new SelectedRollChainSnapshot("MSFT", new DateOnly(2026, 11, 20), DateTimeOffset.UnixEpoch,
            "Test", []);
        Assert.Empty(chain.Contracts);
    }

    private static RollConfiguration Roll() => new()
    {
        Version = new ConfigurationVersion(1),
        MaximumRollDebitPerShare = .25m
    };

    private static OpenShortCallPositionSnapshot Position(decimal? premium) => new(42, Guid.NewGuid(), "MSFT-C", 1,
        100m, new DateOnly(2026, 10, 16), premium, null);

    private static DefenseEvaluationInput Input()
    {
        var position = Position(null);
        var version = new ConfigurationVersion(1);
        return new DefenseEvaluationInput(position,
            new DefenseHoldingContext(position.HoldingId, "MSFT", AssetType.Stock, AssignmentSensitivity.Level3,
                TaxSensitivity.Moderate), null, null, null,
            new EarningsContext(AvailabilityStatus.Unavailable, null), DateTimeOffset.UnixEpoch,
            new DefenseConfiguration { Version = version }, Roll() with { Version = version }, version,
            new DefenseStrategyVersion("6.0.0"), new RollStrategyVersion("6.0.0"));
    }
}
