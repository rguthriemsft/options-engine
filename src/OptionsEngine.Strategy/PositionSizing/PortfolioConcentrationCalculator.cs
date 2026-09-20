using System.Collections.Immutable;
using OptionsEngine.Domain.Accounts;

namespace OptionsEngine.Strategy.PositionSizing;

/// <summary>Explainable result of the pure tracked-account equity concentration calculation.</summary>
public sealed record PortfolioConcentrationResult(
    PortfolioConcentrationStatus Status,
    decimal? TargetHoldingMarketValue,
    decimal? PortfolioMarketValue,
    double? PortfolioWeight,
    double? ConcentrationModifier,
    ImmutableArray<PositionSizingMissingInputCode> MissingInputs,
    string Explanation);

/// <summary>Calculates V1 same-account tracked-equity concentration without data access.</summary>
public sealed class PortfolioConcentrationCalculator
{
    public PortfolioConcentrationResult Calculate(
        PortfolioConcentrationContext context,
        PositionSizingHoldingContext targetHolding,
        PositionSizingConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(targetHolding);
        ArgumentNullException.ThrowIfNull(configuration);
        targetHolding.Validate();
        configuration.Validate();
        ValidateScope(context, targetHolding);

        if (targetHolding.AssetType == AssetType.ExchangeTradedFund)
        {
            return new PortfolioConcentrationResult(
                PortfolioConcentrationStatus.NotApplicable,
                null,
                null,
                null,
                1,
                [],
                "Portfolio concentration is not applicable to exchange-traded funds in V1.");
        }

        if (targetHolding.AssetType == AssetType.Other)
        {
            return new PortfolioConcentrationResult(
                PortfolioConcentrationStatus.InsufficientData,
                null,
                null,
                null,
                null,
                [],
                "The Holding asset type has no approved V1 portfolio concentration behavior.");
        }

        var targetMatches = context.Holdings.IsDefault
            ? []
            : context.Holdings.Where(holding => holding.HoldingId == context.TargetHoldingId).ToArray();
        if (targetMatches.Length != 1)
        {
            return Insufficient(
                PositionSizingMissingInputCode.PortfolioTargetHolding,
                "The target Holding is absent or ambiguous in the supplied concentration context.");
        }

        var targetConcentrationHolding = targetMatches[0];
        if (targetConcentrationHolding.SharesOwned != targetHolding.SharesOwned)
        {
            throw new ArgumentException(
                "The target concentration Holding shares must match the authoritative Position Sizing Holding shares.",
                nameof(context));
        }

        ValidateHoldingIdentities(context.Holdings, context.TargetHoldingId);

        if (context.Holdings.Any(holding => holding.SharesOwned < 0))
            throw new ArgumentOutOfRangeException(nameof(context),
                "Concentration Holding shares cannot be negative.");
        if (context.Holdings.Any(holding =>
                holding.AsOfPrice is null || holding.AsOfPrice <= 0 ||
                holding.PriceObservationDate is null ||
                holding.PriceObservationDate > context.IndicatorAsOfDate))
        {
            return Insufficient(
                PositionSizingMissingInputCode.PortfolioPrice,
                "One or more participating Holdings lack a valid price observation at or before the concentration cutoff.");
        }

        decimal portfolioMarketValue = 0;
        decimal? targetMarketValue = null;
        foreach (var holding in context.Holdings)
        {
            var sharesOwned = holding.HoldingId == context.TargetHoldingId
                ? targetHolding.SharesOwned
                : holding.SharesOwned;
            var marketValue = checked(sharesOwned * holding.AsOfPrice!.Value);
            portfolioMarketValue = checked(portfolioMarketValue + marketValue);
            if (holding.HoldingId == context.TargetHoldingId)
                targetMarketValue = marketValue;
        }

        if (portfolioMarketValue <= 0)
        {
            return new PortfolioConcentrationResult(
                PortfolioConcentrationStatus.InsufficientData,
                targetMarketValue,
                portfolioMarketValue,
                null,
                null,
                [PositionSizingMissingInputCode.PortfolioDenominator],
                "The tracked-account equity concentration denominator is not positive.");
        }

        var portfolioWeight = decimal.ToDouble(targetMarketValue!.Value) /
                              decimal.ToDouble(portfolioMarketValue);
        var modifier = configuration.StockConcentrationModifiers.Resolve(portfolioWeight);
        return new PortfolioConcentrationResult(
            PortfolioConcentrationStatus.Available,
            targetMarketValue,
            portfolioMarketValue,
            portfolioWeight,
            modifier,
            [],
            "Portfolio concentration was calculated from all supplied tracked Holdings in the target Account.");
    }

    private static void ValidateScope(
        PortfolioConcentrationContext context,
        PositionSizingHoldingContext targetHolding)
    {
        if (context.TargetHoldingId != targetHolding.HoldingId)
            throw new ArgumentException(
                "The concentration target Holding must match the Position Sizing Holding.", nameof(context));
        if (context.AccountId != targetHolding.AccountId)
            throw new ArgumentException(
                "The concentration Account must match the Position Sizing Holding Account.", nameof(context));
        if (!context.Holdings.IsDefault && context.Holdings.Any(holding => holding.AccountId != context.AccountId))
            throw new ArgumentException(
                "Every participating concentration Holding must belong to the target Account.", nameof(context));
    }

    private static void ValidateHoldingIdentities(
        ImmutableArray<PortfolioConcentrationHolding> holdings,
        Guid targetHoldingId)
    {
        var duplicate = holdings
            .Where(holding => holding.HoldingId != targetHoldingId)
            .GroupBy(holding => holding.HoldingId)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new ArgumentException(
                $"Concentration Holding {duplicate.Key} is supplied more than once.", nameof(holdings));
    }

    private static PortfolioConcentrationResult Insufficient(
        PositionSizingMissingInputCode missingInput,
        string explanation) => new(
            PortfolioConcentrationStatus.InsufficientData,
            null,
            null,
            null,
            null,
            [missingInput],
            explanation);
}
