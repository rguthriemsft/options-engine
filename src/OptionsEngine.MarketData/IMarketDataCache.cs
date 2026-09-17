using OptionsEngine.MarketData.Models;
namespace OptionsEngine.MarketData;
public interface IMarketDataCache
{
    Task<MarketQuote?> GetLatestQuoteAsync(string symbol, string provider, CancellationToken cancellationToken = default); Task SaveQuoteAsync(MarketQuote quote, CancellationToken cancellationToken = default);
    Task<MarketDataCacheEntry<IReadOnlyList<HistoricalBar>>?> GetHistoricalBarsAsync(string symbol, DateOnly start, DateOnly end, string provider, CancellationToken cancellationToken = default); Task UpsertHistoricalBarsAsync(IReadOnlyList<HistoricalBar> bars, DateTimeOffset retrievedAt, CancellationToken cancellationToken = default);
    Task<MarketDataCacheEntry<IReadOnlyList<DateOnly>>?> GetExpirationsAsync(string symbol, string provider, CancellationToken cancellationToken = default); Task SaveExpirationsAsync(string symbol, string provider, DateTimeOffset retrievedAt, IReadOnlyList<DateOnly> expirations, CancellationToken cancellationToken = default);
    Task<OptionChain?> GetLatestOptionChainAsync(string symbol, DateOnly expiration, string provider, CancellationToken cancellationToken = default); Task SaveOptionChainAsync(OptionChain chain, CancellationToken cancellationToken = default);
}
public sealed record MarketDataCacheEntry<T>(T Value, DateTimeOffset RetrievedAt);
