using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OptionsEngine.MarketData;
using OptionsEngine.MarketData.Models;

namespace OptionsEngine.Infrastructure.Persistence;

public sealed class SqliteMarketDataCache(OptionsEngineDbContext db) : IMarketDataCache
{
    public async Task<MarketQuote?> GetLatestQuoteAsync(string symbol, string provider, CancellationToken cancellationToken = default) => (await db.MarketQuoteSnapshots.AsNoTracking().Where(x => x.Symbol == symbol && x.Provider == provider).ToListAsync(cancellationToken)).OrderByDescending(x => x.Timestamp).Select(ToModel).FirstOrDefault();
    public async Task SaveQuoteAsync(MarketQuote quote, CancellationToken cancellationToken = default) { db.MarketQuoteSnapshots.Add(new MarketQuoteSnapshotEntity { Symbol = quote.Symbol, Provider = quote.Provider, Timestamp = quote.Timestamp, Last = quote.Last, Bid = quote.Bid, Ask = quote.Ask, Open = quote.Open, High = quote.High, Low = quote.Low, PreviousClose = quote.PreviousClose, Volume = quote.Volume, IsDelayed = quote.IsDelayed }); await db.SaveChangesAsync(cancellationToken); }
    public async Task<MarketDataCacheEntry<IReadOnlyList<HistoricalBar>>?> GetHistoricalBarsAsync(string symbol, DateOnly start, DateOnly end, string provider, CancellationToken cancellationToken = default)
    {
        var rows = await db.HistoricalPriceBars.AsNoTracking().Where(x => x.Symbol == symbol && x.Provider == provider && x.Date >= start && x.Date <= end).OrderBy(x => x.Date).ToListAsync(cancellationToken);
        return rows.Count == 0 ? null : new(rows.Select(ToModel).ToArray(), rows.Max(x => x.RetrievedAt));
    }
    public async Task UpsertHistoricalBarsAsync(IReadOnlyList<HistoricalBar> bars, DateTimeOffset retrievedAt, CancellationToken cancellationToken = default)
    {
        foreach (var bar in bars) { var existing = await db.HistoricalPriceBars.SingleOrDefaultAsync(x => x.Symbol == bar.Symbol && x.Date == bar.Date && x.Provider == bar.Provider, cancellationToken); if (existing is null) db.HistoricalPriceBars.Add(new HistoricalPriceBarEntity { Symbol = bar.Symbol, Provider = bar.Provider, Date = bar.Date, Open = bar.Open, High = bar.High, Low = bar.Low, Close = bar.Close, Volume = bar.Volume, RetrievedAt = retrievedAt }); else { existing.Open = bar.Open; existing.High = bar.High; existing.Low = bar.Low; existing.Close = bar.Close; existing.Volume = bar.Volume; existing.RetrievedAt = retrievedAt; } } await db.SaveChangesAsync(cancellationToken);
    }
    public async Task<MarketDataCacheEntry<IReadOnlyList<DateOnly>>?> GetExpirationsAsync(string symbol, string provider, CancellationToken cancellationToken = default) { var value = (await db.OptionExpirationCaches.AsNoTracking().Where(x => x.Symbol == symbol && x.Provider == provider).ToListAsync(cancellationToken)).OrderByDescending(x => x.RetrievedAt).FirstOrDefault(); return value is null ? null : new(JsonSerializer.Deserialize<DateOnly[]>(value.ExpirationsJson) ?? [], value.RetrievedAt); }
    public async Task SaveExpirationsAsync(string symbol, string provider, DateTimeOffset retrievedAt, IReadOnlyList<DateOnly> expirations, CancellationToken cancellationToken = default) { db.OptionExpirationCaches.Add(new OptionExpirationCacheEntity { Symbol = symbol, Provider = provider, RetrievedAt = retrievedAt, ExpirationsJson = JsonSerializer.Serialize(expirations) }); await db.SaveChangesAsync(cancellationToken); }
    public async Task<OptionChain?> GetLatestOptionChainAsync(string symbol, DateOnly expiration, string provider, CancellationToken cancellationToken = default)
    {
        var rows = await db.OptionContractSnapshots.AsNoTracking().Where(x => x.UnderlyingSymbol == symbol && x.Expiration == expiration && x.Provider == provider).ToListAsync(cancellationToken); if (rows.Count == 0) return null; var timestamp = rows.Max(x => x.Timestamp); return new OptionChain(symbol, expiration, timestamp, rows.Where(x => x.Timestamp == timestamp).Select(ToModel).ToArray(), provider);
    }
    public async Task SaveOptionChainAsync(OptionChain chain, CancellationToken cancellationToken = default) { db.OptionContractSnapshots.AddRange(chain.Contracts.Select(x => new OptionContractSnapshotEntity { OptionSymbol = x.OptionSymbol, UnderlyingSymbol = x.UnderlyingSymbol, Provider = x.Provider, Timestamp = x.Timestamp, Expiration = x.Expiration, Strike = x.Strike, OptionType = x.OptionType, Bid = x.Bid, Ask = x.Ask, Last = x.Last, Volume = x.Volume, OpenInterest = x.OpenInterest, ImpliedVolatility = x.ImpliedVolatility, Delta = x.Delta, Gamma = x.Gamma, Theta = x.Theta, Vega = x.Vega, UnderlyingPrice = x.UnderlyingPrice })); await db.SaveChangesAsync(cancellationToken); }
    private static MarketQuote ToModel(MarketQuoteSnapshotEntity x) => new(x.Symbol, x.Timestamp, x.Last, x.Bid, x.Ask, x.Open, x.High, x.Low, x.PreviousClose, x.Volume, x.Provider, x.IsDelayed);
    private static HistoricalBar ToModel(HistoricalPriceBarEntity x) => new(x.Symbol, x.Date, x.Open, x.High, x.Low, x.Close, x.Volume, x.Provider);
    private static OptionContractSnapshot ToModel(OptionContractSnapshotEntity x) => new(x.OptionSymbol, x.UnderlyingSymbol, x.Timestamp, x.Expiration, x.Strike, x.OptionType, x.Bid, x.Ask, x.Last, x.Volume, x.OpenInterest, x.ImpliedVolatility, x.Delta, x.Gamma, x.Theta, x.Vega, x.UnderlyingPrice, x.Provider);
}
