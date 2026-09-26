using OptionsEngine.Strategy.Defense;
using OptionsEngine.Strategy.Indicators;
using OptionsEngine.Strategy.EntryStrategy;
using OptionsEngine.Domain.Accounts;

namespace OptionsEngine.Strategy.Tests;

public sealed class DefenseContractTests
{
    [Fact]
    public void FinalCandidateStatesContainNoTemporaryPhaseSixCState()
    {
        Assert.Equal(
        [
            RollCandidateEvaluationState.Rejected,
            RollCandidateEvaluationState.Rankable,
            RollCandidateEvaluationState.InsufficientData
        ], Enum.GetValues<RollCandidateEvaluationState>());
    }

    [Fact]
    public void ApprovedDefaultConfigurationStructureValidates()
    {
        new DefenseConfiguration { Version = new ConfigurationVersion(1) }.Validate();
        var roll = Roll();
        roll.Validate();
        Assert.Equal(.10, roll.FullStrikeImprovementRatio);
        Assert.Equal(.25, roll.FullCreditEconomicsRatio);
        Assert.Equal(55, roll.CurrentCcosRollThreshold);
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
    public void RollConfigurationUsesResolvedPhaseFourLiquidityOnly()
    {
        var liquidityScoring = new ContractLiquidityScoringConfiguration
        {
            SpreadPercent = new ContinuousScoreTable { Bands =
            [
                new(null, false, .04, true, 5), new(.04, false, .08, true, 4),
                new(.08, false, .12, true, 2), new(.12, false, .15, true, 1)
            ] },
            OpenInterest = new IntegerScoreTable { Bands =
            [
                new(250, 499, 1), new(500, 999, 2), new(1000, 1999, 3),
                new(2000, 3999, 4), new(4000, null, 5)
            ] }
        };
        var phaseFour = new EntryStrategyConfiguration
        {
            Version = new ConfigurationVersion(1),
            ContractEligibility = new ContractEligibilityConfiguration
            {
                MinimumOpenInterest = 250,
                MaximumBidAskSpreadPercent = .15
            },
            ContractScore = new ContractScoreConfiguration { Liquidity = liquidityScoring }
        };
        phaseFour.Validate();

        var resolved = Roll().WithLiquidityFrom(phaseFour);

        Assert.Equal(250, resolved.LiquidityEligibility.MinimumOpenInterest);
        Assert.Equal(.15, resolved.LiquidityEligibility.MaximumBidAskSpreadPercent);
        Assert.Same(liquidityScoring, resolved.LiquidityScoring);
        Assert.Equal(21, resolved.MinimumReplacementDte);
        Assert.Equal(.25, resolved.NormalMaximumDelta);
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
        var observation = Observation(delta: -.25);
        Assert.Equal(-.25, observation.Delta);
    }

    [Fact]
    public void ReplacementObservationPreservesContractIdentityAndTerms()
    {
        var observation = Observation();

        Assert.Equal("MSFT-C", observation.OptionSymbol);
        Assert.Equal("MSFT", observation.UnderlyingSymbol);
        Assert.Equal(DateTimeOffset.UnixEpoch, observation.ObservationTimestampUtc);
        Assert.Equal(new DateOnly(2026, 11, 20), observation.Expiration);
        Assert.Equal(110m, observation.Strike);
        Assert.Equal(OptionContractType.Call, observation.OptionType);
        Assert.Equal("Test", observation.Provider);
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
        chain.Validate();
        Assert.Empty(chain.Contracts);
    }

    [Fact]
    public void SelectedRollChainRejectsContractFromDifferentSnapshot()
    {
        var chain = new SelectedRollChainSnapshot("MSFT", new DateOnly(2026, 11, 20), DateTimeOffset.UnixEpoch,
            "Test", [Observation() with { Expiration = new DateOnly(2026, 12, 18) }]);

        Assert.Throws<ArgumentException>(chain.Validate);
    }

    private static RollConfiguration Roll() => new()
    {
        Version = new ConfigurationVersion(1),
        MaximumRollDebitPerShare = .25m
    };

    private static OpenShortCallPositionSnapshot Position(decimal? premium) => new(42, Guid.NewGuid(), "MSFT-C", 1,
        100m, new DateOnly(2026, 10, 16), premium, null);

    private static DefenseOptionObservation Observation(double? delta = .15) => new("MSFT-C", "MSFT",
        DateTimeOffset.UnixEpoch, new DateOnly(2026, 11, 20), 110m, OptionContractType.Call,
        1m, 1.10m, 1.05m, 250, .30, delta, .02, -.01, .10, 100m, "Test");

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
