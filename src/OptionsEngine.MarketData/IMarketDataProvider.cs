using OptionsEngine.MarketData.Models;
namespace OptionsEngine.MarketData;
public interface IMarketDataProvider
{
    string ProviderName { get; }
    Task<MarketQuote> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<HistoricalBar>> GetHistoricalPricesAsync(string symbol, DateOnly start, DateOnly end, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DateOnly>> GetOptionExpirationsAsync(string symbol, CancellationToken cancellationToken = default);
    Task<OptionChain> GetOptionChainAsync(string symbol, DateOnly expiration, CancellationToken cancellationToken = default);
}
