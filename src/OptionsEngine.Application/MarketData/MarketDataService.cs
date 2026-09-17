using Microsoft.Extensions.Logging;
using OptionsEngine.MarketData;
using OptionsEngine.MarketData.Models;

namespace OptionsEngine.Application.MarketData;

/// <summary>Coordinates provider-independent retrieval with the SQLite market-data cache.</summary>
public sealed class MarketDataService(IMarketDataProvider provider, IMarketDataCache cache, MarketDataCacheOptions cacheOptions, ILogger<MarketDataService> logger)
{
    private readonly IMarketDataProvider provider = provider;
    private readonly IMarketDataCache cache = cache;
    private readonly MarketDataCacheOptions cacheOptions = cacheOptions;
    private readonly ILogger<MarketDataService> logger = logger;
    private const string Provider = "Tradier";

    public async Task<MarketQuote> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(symbol); var cached = await cache.GetLatestQuoteAsync(normalized, Provider, cancellationToken);
        if (cached is not null && DateTimeOffset.UtcNow - cached.Timestamp <= cacheOptions.QuoteFreshness) { logger.LogInformation("Market cache hit for quote {Symbol}", normalized); return cached; }
        var quote = await provider.GetQuoteAsync(normalized, cancellationToken); await cache.SaveQuoteAsync(quote, cancellationToken); return quote;
    }
    public async Task<IReadOnlyList<HistoricalBar>> GetHistoricalPricesAsync(string symbol, DateOnly start, DateOnly end, CancellationToken cancellationToken = default)
    {
        if (start > end) throw new MarketDataException(MarketDataFailureKind.InvalidRequest, "Start date must be on or before end date.");
        var normalized = Normalize(symbol); var cached = await cache.GetHistoricalBarsAsync(normalized, start, end, Provider, cancellationToken);
        if (cached is not null && DateTimeOffset.UtcNow - cached.RetrievedAt <= cacheOptions.HistoricalBarsFreshness) return cached.Value;
        var bars = await provider.GetHistoricalPricesAsync(normalized, start, end, cancellationToken); await cache.UpsertHistoricalBarsAsync(bars, DateTimeOffset.UtcNow, cancellationToken); return bars;
    }
    public async Task<IReadOnlyList<DateOnly>> GetOptionExpirationsAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(symbol); var cached = await cache.GetExpirationsAsync(normalized, Provider, cancellationToken);
        if (cached is not null && DateTimeOffset.UtcNow - cached.RetrievedAt <= cacheOptions.OptionExpirationsFreshness) return cached.Value;
        var dates = await provider.GetOptionExpirationsAsync(normalized, cancellationToken); await cache.SaveExpirationsAsync(normalized, Provider, DateTimeOffset.UtcNow, dates, cancellationToken); return dates;
    }
    public async Task<OptionChain> GetOptionChainAsync(string symbol, DateOnly expiration, CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(symbol); var cached = await cache.GetLatestOptionChainAsync(normalized, expiration, Provider, cancellationToken);
        if (cached is not null && DateTimeOffset.UtcNow - cached.Timestamp <= cacheOptions.OptionChainFreshness) return cached;
        var chain = await provider.GetOptionChainAsync(normalized, expiration, cancellationToken); await cache.SaveOptionChainAsync(chain, cancellationToken); return chain;
    }
    private static string Normalize(string symbol) => string.IsNullOrWhiteSpace(symbol) ? throw new MarketDataException(MarketDataFailureKind.InvalidSymbol, "A market symbol is required.") : symbol.Trim().ToUpperInvariant();
}
