using System.Collections.Immutable;

namespace OptionsEngine.Strategy.EntryStrategy;

public enum ContractLiquidityEligibilityStatus
{
    Passed,
    Failed,
    InsufficientData
}

public enum ContractLiquidityMissingInput
{
    Bid,
    Ask,
    OpenInterest
}

public sealed record ContractLiquidityEligibilityResult(
    ContractLiquidityEligibilityStatus Status,
    double? BidAskSpreadPercent,
    ImmutableArray<ContractLiquidityMissingInput> MissingInputs);

/// <summary>Shared Phase 4/6 V1 liquidity hard-gate semantics.</summary>
public static class ContractLiquidityEvaluator
{
    public static ContractLiquidityEligibilityResult Evaluate(decimal? bid, decimal? ask, long? openInterest,
        ContractLiquidityEligibilityConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        configuration.Validate();
        var missing = ImmutableArray.CreateBuilder<ContractLiquidityMissingInput>();
        if (bid is null) missing.Add(ContractLiquidityMissingInput.Bid);
        if (ask is null) missing.Add(ContractLiquidityMissingInput.Ask);
        if (openInterest is null) missing.Add(ContractLiquidityMissingInput.OpenInterest);
        var spread = BidAskSpreadPercent(bid, ask);
        if (missing.Count > 0)
            return new ContractLiquidityEligibilityResult(ContractLiquidityEligibilityStatus.InsufficientData,
                spread, missing.ToImmutable());

        var passed = bid > 0 && ask > bid &&
                     openInterest >= configuration.MinimumOpenInterest &&
                     spread <= configuration.MaximumBidAskSpreadPercent;
        return new ContractLiquidityEligibilityResult(
            passed ? ContractLiquidityEligibilityStatus.Passed : ContractLiquidityEligibilityStatus.Failed,
            spread, []);
    }

    public static double? BidAskSpreadPercent(decimal? bid, decimal? ask)
    {
        var mid = bid is { } bidValue && ask is { } askValue ? (bidValue + askValue) / 2m : (decimal?)null;
        return mid is > 0 && bid is { } spreadBid && ask is { } spreadAsk
            ? (double)((spreadAsk - spreadBid) / mid.Value)
            : null;
    }
}
