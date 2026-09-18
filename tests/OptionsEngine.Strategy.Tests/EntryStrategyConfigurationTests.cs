using OptionsEngine.Domain.Accounts;
using OptionsEngine.Strategy.EntryStrategy;
using OptionsEngine.Strategy.Indicators;

namespace OptionsEngine.Strategy.Tests;

public sealed class EntryStrategyConfigurationTests
{
    [Fact]
    public void DefaultConfigurationIsValidAndUsesSpecifiedPhase4Defaults()
    {
        var configuration = Configuration();

        configuration.Validate(Holding());

        Assert.Equal(14, configuration.ContractEligibility.MinimumDte);
        Assert.Equal(45, configuration.ContractEligibility.MaximumDte);
        Assert.Equal(.25, configuration.ContractEligibility.GlobalMaximumInitialDelta);
        Assert.Equal(.20, configuration.ContractEligibility.HighTaxMaximumDelta);
        Assert.Equal(100, configuration.ContractEligibility.MinimumOpenInterest);
        Assert.Equal(.20, configuration.ContractEligibility.MaximumBidAskSpreadPercent);
        Assert.Equal(70, configuration.Ccos.DefaultMinimumCcos);
    }

    [Theory]
    [InlineData(0, 45)]
    [InlineData(45, 14)]
    public void InvalidDteLimitsFailClearly(int minimum, int maximum)
    {
        var configuration = Configuration() with { ContractEligibility = new ContractEligibilityConfiguration { MinimumDte = minimum, MaximumDte = maximum } };

        Assert.Throws<ArgumentOutOfRangeException>(configuration.Validate);
    }

    [Fact]
    public void InvalidLiquidityAndDeltaConfigurationFailClearly()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => (Configuration() with
        {
            ContractEligibility = new ContractEligibilityConfiguration { GlobalMaximumInitialDelta = double.NaN }
        }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (Configuration() with
        {
            ContractEligibility = new ContractEligibilityConfiguration { MaximumBidAskSpreadPercent = -0.01 }
        }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (Configuration() with
        {
            ContractEligibility = new ContractEligibilityConfiguration { MinimumOpenInterest = -1 }
        }).Validate());
    }

    [Fact]
    public void ComponentMaximumScoresMustRemainACompleteHundredPointScale()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => (Configuration() with
        {
            Ccos = new CcosConfiguration { VolatilityMaximumScore = 24 }
        }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (Configuration() with
        {
            ContractScore = new ContractScoreConfiguration { ThetaEfficiencyMaximumScore = double.PositiveInfinity }
        }).Validate());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.01)]
    [InlineData(double.NaN)]
    public void MinimumAnnualizedYieldMustBeFiniteAndGreaterThanZero(double minimumAnnualizedYield)
    {
        var holding = Holding() with { MinimumAnnualizedYield = minimumAnnualizedYield };

        Assert.Throws<ArgumentOutOfRangeException>(() => Configuration().Validate(holding));
    }

    [Fact]
    public void PreferredDeltaMinimumCannotExceedEffectivePreferredMaximum()
    {
        var holding = Holding() with { PreferredDeltaMinimum = .26, PreferredDeltaMaximum = .30, MaximumInitialDelta = .30 };

        Assert.Throws<ArgumentException>(() => Configuration().Validate(holding));
    }

    [Fact]
    public void HighTaxCapParticipatesInPreferredDeltaValidation()
    {
        var holding = Holding() with
        {
            TaxSensitivity = TaxSensitivity.High,
            MaximumInitialDelta = .25,
            PreferredDeltaMinimum = .21,
            PreferredDeltaMaximum = .25
        };

        Assert.Throws<ArgumentException>(() => Configuration().Validate(holding));
    }

    [Fact]
    public void AvailableAndUnavailableResultStatesDoNotRequireNumericSentinels()
    {
        var unavailableInput = IndicatorValue<double>.InsufficientData();
        var gate = new GateResult(GateCode.Liquidity, GateStatus.Unavailable, RejectionReasonCode.InsufficientData,
            [], [MissingInputCode.OptionBid], "Bid was not supplied.");

        Assert.Null(unavailableInput.Value);
        Assert.Equal(IndicatorValueStatus.InsufficientData, unavailableInput.Status);
        Assert.Equal(GateStatus.Unavailable, gate.Status);
        Assert.Contains(MissingInputCode.OptionBid, gate.MissingInputs);
    }

    private static EntryStrategyConfiguration Configuration() => new()
    {
        Version = new ConfigurationVersion(1),
        EffectiveDate = new DateOnly(2026, 9, 17)
    };

    private static HoldingContext Holding() => new(
        Guid.Parse("1B539C36-CA1A-4FB0-B34A-84C821258AAB"),
        "MSFT",
        AssetType.Stock,
        AssignmentSensitivity.Level3,
        TaxSensitivity.Moderate,
        .25,
        .12,
        .18,
        70,
        70,
        0.10m,
        .10);
}
