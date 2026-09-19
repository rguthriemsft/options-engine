using OptionsEngine.Domain.Accounts;
using OptionsEngine.Strategy.Indicators;
using OptionsEngine.Strategy.PositionSizing;

namespace OptionsEngine.Strategy.Tests;

public sealed class PortfolioConcentrationCalculatorTests
{
    private static readonly Guid TargetHoldingId = Guid.Parse("24F4EA2D-C1A6-4424-8C83-D5D0248E78FC");
    private static readonly Guid OtherHoldingId = Guid.Parse("AD4B70C8-B21D-47CF-AC94-1E0487553E3D");
    private static readonly Guid AccountId = Guid.Parse("3E8B226E-197D-4A85-A9A3-E840ED92B460");
    private static readonly Guid OtherAccountId = Guid.Parse("DF9BF495-3E9F-4210-A8E1-A43D9CF7C3DA");
    private static readonly DateOnly AsOfDate = new(2026, 9, 18);
    private static readonly PortfolioConcentrationCalculator Calculator = new();
    private static readonly PositionSizingConfiguration Configuration = new()
    {
        Version = new ConfigurationVersion(1)
    };

    [Theory]
    [InlineData(.049999, 1.10)]
    [InlineData(.05, 1.00)]
    [InlineData(.050001, 1.00)]
    [InlineData(.149999, 1.00)]
    [InlineData(.15, .90)]
    [InlineData(.150001, .90)]
    [InlineData(.249999, .90)]
    [InlineData(.25, .80)]
    [InlineData(.250001, .80)]
    [InlineData(.399999, .80)]
    [InlineData(.40, .80)]
    [InlineData(.400001, .70)]
    public void StockThresholdsUseApprovedBelowExactAndAboveSemantics(
        double weight,
        double expectedModifier)
    {
        var context = ContextAtWeight((decimal)weight);
        var result = Calculator.Calculate(context, MatchingHolding(context), Configuration);

        Assert.Equal(PortfolioConcentrationStatus.Available, result.Status);
        Assert.Equal(weight, result.PortfolioWeight!.Value, 8);
        Assert.Equal(expectedModifier, result.ConcentrationModifier);
        Assert.Empty(result.MissingInputs);
    }

    [Fact]
    public void FormulaUsesActualSharesAndPricesWithoutContractRounding()
    {
        var context = Context(
            new PortfolioConcentrationHolding(TargetHoldingId, AccountId, "MSFT", 100m, 200m, AsOfDate),
            new PortfolioConcentrationHolding(OtherHoldingId, AccountId, "OTHER", 400m, 200m, AsOfDate));

        var result = Calculator.Calculate(context, Holding(shares: 100m), Configuration);

        Assert.Equal(20_000m, result.TargetHoldingMarketValue);
        Assert.Equal(100_000m, result.PortfolioMarketValue);
        Assert.Equal(.20, result.PortfolioWeight);
        Assert.Equal(.90, result.ConcentrationModifier);
    }

    [Fact]
    public void TargetConcentrationSharesMustMatchAuthoritativeSizingShares()
    {
        var context = Context(
            new PortfolioConcentrationHolding(TargetHoldingId, AccountId, "MSFT", 101m, 200m, AsOfDate),
            new PortfolioConcentrationHolding(OtherHoldingId, AccountId, "OTHER", 400m, 200m, AsOfDate));

        var exception = Assert.Throws<ArgumentException>(() =>
            Calculator.Calculate(context, Holding(shares: 100m), Configuration));

        Assert.Contains("authoritative Position Sizing Holding shares", exception.Message);
    }

    [Fact]
    public void ModifierComesFromResolvedConfigurationTable()
    {
        var configuration = Configuration with
        {
            StockConcentrationModifiers = new PositionSizingBandTable
            {
                Bands = [new PositionSizingBand(0, true, 1, true, .42)]
            }
        };

        var context = ContextAtWeight(.20m);
        var result = Calculator.Calculate(context, MatchingHolding(context), configuration);

        Assert.Equal(.42, result.ConcentrationModifier);
    }

    [Fact]
    public void HoldingEnumerationOrderCannotChangeResult()
    {
        var target = new PortfolioConcentrationHolding(TargetHoldingId, AccountId, "MSFT", 100m, 200m, AsOfDate);
        var other = new PortfolioConcentrationHolding(OtherHoldingId, AccountId, "OTHER", 400m, 200m, AsOfDate);

        var first = Calculator.Calculate(Context(target, other), Holding(shares: 100m), Configuration);
        var second = Calculator.Calculate(Context(other, target), Holding(shares: 100m), Configuration);

        Assert.Equal(first, second);
    }

    [Fact]
    public void ConcentrationContractHasNoStrategyEnabledFilter()
    {
        var properties = typeof(PortfolioConcentrationHolding).GetProperties()
            .Select(property => property.Name);

        Assert.DoesNotContain("IsEnabled", properties);
        Assert.DoesNotContain("StrategyEnabled", properties);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MissingAnyParticipatingPriceMakesStockConcentrationUnavailable(bool targetPriceMissing)
    {
        var target = new PortfolioConcentrationHolding(TargetHoldingId, AccountId, "MSFT", 100m,
            targetPriceMissing ? null : 200m, AsOfDate);
        var other = new PortfolioConcentrationHolding(OtherHoldingId, AccountId, "OTHER", 400m,
            targetPriceMissing ? 200m : null, AsOfDate);

        var result = Calculator.Calculate(Context(target, other), Holding(shares: 100m), Configuration);

        Assert.Equal(PortfolioConcentrationStatus.InsufficientData, result.Status);
        Assert.Equal(PositionSizingMissingInputCode.PortfolioPrice, Assert.Single(result.MissingInputs));
        Assert.Null(result.PortfolioMarketValue);
        Assert.Null(result.PortfolioWeight);
        Assert.Null(result.ConcentrationModifier);
    }

    [Fact]
    public void FutureDatedPriceIsNotConsumedOrSubstituted()
    {
        var context = Context(
            new PortfolioConcentrationHolding(TargetHoldingId, AccountId, "MSFT", 100m, 200m, AsOfDate),
            new PortfolioConcentrationHolding(OtherHoldingId, AccountId, "OTHER", 400m, 200m,
                AsOfDate.AddDays(1)));

        var result = Calculator.Calculate(context, Holding(shares: 100m), Configuration);

        Assert.Equal(PortfolioConcentrationStatus.InsufficientData, result.Status);
        Assert.Equal(PositionSizingMissingInputCode.PortfolioPrice, Assert.Single(result.MissingInputs));
        Assert.Null(result.PortfolioWeight);
        Assert.Null(result.ConcentrationModifier);
    }

    [Fact]
    public void OlderEligiblePriceHasNoUndocumentedFreshnessRejection()
    {
        var context = Context(
            new PortfolioConcentrationHolding(TargetHoldingId, AccountId, "MSFT", 100m, 200m,
                AsOfDate.AddYears(-10)),
            new PortfolioConcentrationHolding(OtherHoldingId, AccountId, "OTHER", 400m, 200m,
                AsOfDate.AddYears(-10)));

        var result = Calculator.Calculate(context, Holding(shares: 100m), Configuration);

        Assert.Equal(PortfolioConcentrationStatus.Available, result.Status);
        Assert.Equal(.20, result.PortfolioWeight);
        Assert.Equal(.90, result.ConcentrationModifier);
    }

    [Fact]
    public void MissingObservationDateCannotValidateAsOfPrice()
    {
        var context = Context(
            new PortfolioConcentrationHolding(TargetHoldingId, AccountId, "MSFT", 100m, 200m, AsOfDate),
            new PortfolioConcentrationHolding(OtherHoldingId, AccountId, "OTHER", 400m, 200m, null));

        var result = Calculator.Calculate(context, Holding(shares: 100m), Configuration);

        Assert.Equal(PositionSizingMissingInputCode.PortfolioPrice, Assert.Single(result.MissingInputs));
        Assert.Null(result.ConcentrationModifier);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositivePriceIsUnavailableRatherThanConsumed(double price)
    {
        var context = Context(
            new PortfolioConcentrationHolding(TargetHoldingId, AccountId, "MSFT", 100m, 200m, AsOfDate),
            new PortfolioConcentrationHolding(OtherHoldingId, AccountId, "OTHER", 400m, (decimal)price, AsOfDate));

        var result = Calculator.Calculate(context, Holding(shares: 100m), Configuration);

        Assert.Equal(PortfolioConcentrationStatus.InsufficientData, result.Status);
        Assert.Equal(PositionSizingMissingInputCode.PortfolioPrice, Assert.Single(result.MissingInputs));
        Assert.Null(result.PortfolioWeight);
        Assert.Null(result.ConcentrationModifier);
    }

    [Fact]
    public void ZeroDenominatorIsUnavailableRatherThanRepaired()
    {
        var context = Context(
            new PortfolioConcentrationHolding(TargetHoldingId, AccountId, "MSFT", 0m, 200m, AsOfDate),
            new PortfolioConcentrationHolding(OtherHoldingId, AccountId, "OTHER", 0m, 200m, AsOfDate));

        var result = Calculator.Calculate(context, Holding(shares: 0), Configuration);

        Assert.Equal(PortfolioConcentrationStatus.InsufficientData, result.Status);
        Assert.Equal(PositionSizingMissingInputCode.PortfolioDenominator, Assert.Single(result.MissingInputs));
        Assert.Equal(0m, result.TargetHoldingMarketValue);
        Assert.Equal(0m, result.PortfolioMarketValue);
        Assert.Null(result.PortfolioWeight);
        Assert.Null(result.ConcentrationModifier);
    }

    [Fact]
    public void EtfIsNeutralAndDoesNotRequireStockPricesOrFabricateWeight()
    {
        var context = new PortfolioConcentrationContext(TargetHoldingId, AccountId, AsOfDate, []);

        var result = Calculator.Calculate(
            context,
            Holding(assetType: AssetType.ExchangeTradedFund),
            Configuration);

        Assert.Equal(PortfolioConcentrationStatus.NotApplicable, result.Status);
        Assert.Null(result.TargetHoldingMarketValue);
        Assert.Null(result.PortfolioMarketValue);
        Assert.Null(result.PortfolioWeight);
        Assert.Equal(1, result.ConcentrationModifier);
        Assert.Empty(result.MissingInputs);
    }

    [Fact]
    public void OtherAssetTypeIsExplicitlyUnsupported()
    {
        var result = Calculator.Calculate(
            ContextAtWeight(.20m),
            Holding(assetType: AssetType.Other),
            Configuration);

        Assert.Equal(PortfolioConcentrationStatus.InsufficientData, result.Status);
        Assert.Null(result.PortfolioWeight);
        Assert.Null(result.ConcentrationModifier);
    }

    [Fact]
    public void MismatchedTargetAccountFailsStructuralValidation()
    {
        var context = new PortfolioConcentrationContext(TargetHoldingId, OtherAccountId, AsOfDate, []);

        Assert.Throws<ArgumentException>(() => Calculator.Calculate(context, Holding(), Configuration));
    }

    [Fact]
    public void ParticipatingHoldingFromAnotherAccountFailsStructuralValidation()
    {
        var context = Context(
            new PortfolioConcentrationHolding(TargetHoldingId, AccountId, "MSFT", 100m, 200m, AsOfDate),
            new PortfolioConcentrationHolding(OtherHoldingId, OtherAccountId, "OTHER", 400m, 200m, AsOfDate));

        Assert.Throws<ArgumentException>(() => Calculator.Calculate(context, Holding(), Configuration));
    }

    [Fact]
    public void ContextTargetMustMatchPositionSizingHoldingIdentity()
    {
        var context = new PortfolioConcentrationContext(OtherHoldingId, AccountId, AsOfDate, []);

        Assert.Throws<ArgumentException>(() => Calculator.Calculate(context, Holding(), Configuration));
    }

    [Fact]
    public void MissingTargetHoldingIsExplicitlyUnavailable()
    {
        var context = Context(
            new PortfolioConcentrationHolding(OtherHoldingId, AccountId, "OTHER", 400m, 200m, AsOfDate));

        var result = Calculator.Calculate(context, Holding(), Configuration);

        Assert.Equal(PortfolioConcentrationStatus.InsufficientData, result.Status);
        Assert.Equal(PositionSizingMissingInputCode.PortfolioTargetHolding, Assert.Single(result.MissingInputs));
        Assert.Null(result.PortfolioWeight);
    }

    [Fact]
    public void AmbiguousTargetHoldingIsExplicitlyUnavailable()
    {
        var target = new PortfolioConcentrationHolding(TargetHoldingId, AccountId, "MSFT", 100m, 200m, AsOfDate);
        var context = Context(target, target);

        var result = Calculator.Calculate(context, Holding(), Configuration);

        Assert.Equal(PortfolioConcentrationStatus.InsufficientData, result.Status);
        Assert.Equal(PositionSizingMissingInputCode.PortfolioTargetHolding, Assert.Single(result.MissingInputs));
        Assert.Null(result.PortfolioWeight);
    }

    private static PortfolioConcentrationContext ContextAtWeight(decimal weight)
    {
        const decimal totalMarketValue = 100_000m;
        var targetMarketValue = totalMarketValue * weight;
        return Context(
            new PortfolioConcentrationHolding(
                TargetHoldingId, AccountId, "MSFT", targetMarketValue, 1m, AsOfDate),
            new PortfolioConcentrationHolding(
                OtherHoldingId, AccountId, "OTHER", totalMarketValue - targetMarketValue, 1m, AsOfDate));
    }

    private static PortfolioConcentrationContext Context(params PortfolioConcentrationHolding[] holdings) =>
        new(TargetHoldingId, AccountId, AsOfDate, [.. holdings]);

    private static PositionSizingHoldingContext MatchingHolding(PortfolioConcentrationContext context) =>
        Holding(context.Holdings.Single(holding => holding.HoldingId == TargetHoldingId).SharesOwned);

    private static PositionSizingHoldingContext Holding(
        decimal shares = 100m,
        AssetType assetType = AssetType.Stock) => new(
            TargetHoldingId,
            AccountId,
            "MSFT",
            assetType,
            shares,
            AssignmentSensitivity.Level3,
            TaxSensitivity.Moderate,
            .80m,
            .20);
}
