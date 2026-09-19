using OptionsEngine.Domain.Accounts;
using OptionsEngine.MarketData.Models;
using OptionsEngine.Strategy.EntryStrategy;
using OptionsEngine.Strategy.Indicators;

namespace OptionsEngine.Application.EntryStrategy;

/// <summary>Application-owned source of holdings used to assemble an immutable strategy snapshot.</summary>
public interface IHoldingRepository
{
    Task<Holding?> GetByIdAsync(Guid holdingId, CancellationToken cancellationToken = default);
}

/// <summary>Provider-independent boundary for the next known stock earnings date at an evaluation cutoff.</summary>
public interface IEarningsDateSource
{
    Task<DateOnly?> GetNextEarningsDateAsync(string symbol, DateTimeOffset evaluationTimestampUtc,
        CancellationToken cancellationToken = default);
}

/// <summary>All server-owned versions and resolved configurations consumed by one Phase 4 evaluation.</summary>
public sealed record EntryStrategyOrchestrationConfiguration
{
    public required EntryStrategyConfiguration StrategyConfiguration { get; init; }
    public required IndicatorConfiguration IndicatorConfiguration { get; init; }
    public required IndicatorCalculationVersion IndicatorCalculationVersion { get; init; }
    public required StrategyVersion StrategyVersion { get; init; }

    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(StrategyConfiguration);
        ArgumentNullException.ThrowIfNull(IndicatorConfiguration);
        ArgumentNullException.ThrowIfNull(IndicatorCalculationVersion);
        ArgumentNullException.ThrowIfNull(StrategyVersion);
        StrategyConfiguration.Validate();
        IndicatorConfiguration.Validate();
        if (StrategyConfiguration.Version != IndicatorConfiguration.Version)
            throw new ArgumentException("Phase 3 and Phase 4 must use the same configuration version.", nameof(StrategyConfiguration));
    }
}

/// <summary>Phase 4D's reproducible, non-persisted handoff to the later Phase 4E persistence packet.</summary>
public sealed record EntryStrategyEvaluationBundle(
    EvaluationContext Context,
    IReadOnlyList<OptionChain> SelectedOptionChains,
    IReadOnlyList<OptionContractContext> SelectedContracts,
    ContractStrategyResult StrategyResult);

public sealed class HoldingNotFoundException(Guid holdingId)
    : InvalidOperationException($"Holding '{holdingId}' was not found.");

public sealed class HoldingDisabledException(Guid holdingId)
    : InvalidOperationException($"Holding '{holdingId}' is disabled.");

public sealed class IndicatorDataUnavailableException(string symbol)
    : InvalidOperationException($"No applicable Phase 3 indicator data is available for '{symbol}'.");
