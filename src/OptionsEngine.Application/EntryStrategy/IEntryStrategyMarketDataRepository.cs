using OptionsEngine.MarketData.Models;

namespace OptionsEngine.Application.EntryStrategy;

/// <summary>Reads complete persisted option-chain observations for a Phase 4 evaluation cutoff.</summary>
public interface IEntryStrategyMarketDataRepository
{
    Task<IReadOnlyList<OptionChain>> GetOptionChainsAsync(
        string symbol,
        string provider,
        DateOnly minimumExpiration,
        DateOnly maximumExpiration,
        DateTimeOffset evaluationTimestampUtc,
        CancellationToken cancellationToken = default);
}
