using Microsoft.EntityFrameworkCore;
using OptionsEngine.Application.EntryStrategy;
using OptionsEngine.MarketData.Models;

namespace OptionsEngine.Infrastructure.Persistence;

/// <summary>Phase 4 option-observation query: eligibility is the UTC evaluation cutoff, not Phase 3's as-of date rule.</summary>
public sealed class SqliteEntryStrategyMarketDataRepository(OptionsEngineDbContext db) : IEntryStrategyMarketDataRepository
{
    public async Task<IReadOnlyList<OptionChain>> GetOptionChainsAsync(string symbol, string provider,
        DateOnly minimumExpiration, DateOnly maximumExpiration, DateTimeOffset evaluationTimestampUtc,
        CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(symbol);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        if (minimumExpiration > maximumExpiration)
            throw new ArgumentException("Minimum expiration must not be after maximum expiration.", nameof(minimumExpiration));
        if (evaluationTimestampUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Evaluation timestamp must be UTC.", nameof(evaluationTimestampUtc));

        var cutoffTicks = evaluationTimestampUtc.UtcTicks;
        var rows = await db.OptionContractSnapshots.AsNoTracking()
            .Where(x => x.UnderlyingSymbol == normalized && x.Provider == provider &&
                x.Expiration >= minimumExpiration && x.Expiration <= maximumExpiration && x.TimestampUtcTicks <= cutoffTicks)
            .OrderBy(x => x.Expiration).ThenBy(x => x.TimestampUtcTicks).ThenBy(x => x.OptionSymbol)
            .ToListAsync(cancellationToken);
        var emptyRows = await db.EmptyOptionChainSnapshots.AsNoTracking()
            .Where(x => x.UnderlyingSymbol == normalized && x.Provider == provider &&
                x.Expiration >= minimumExpiration && x.Expiration <= maximumExpiration && x.TimestampUtcTicks <= cutoffTicks)
            .OrderBy(x => x.Expiration).ThenBy(x => x.TimestampUtcTicks)
            .ToListAsync(cancellationToken);

        return rows.GroupBy(x => new { x.Expiration, x.TimestampUtcTicks })
            .Select(group => new OptionChain(normalized, group.Key.Expiration, group.First().Timestamp,
                group.Select(x => new OptionContractSnapshot(x.OptionSymbol, x.UnderlyingSymbol, x.Timestamp, x.Expiration,
                    x.Strike, x.OptionType, x.Bid, x.Ask, x.Last, x.Volume, x.OpenInterest, x.ImpliedVolatility,
                    x.Delta, x.Gamma, x.Theta, x.Vega, x.UnderlyingPrice, x.Provider)).ToArray(), provider))
            .Concat(emptyRows.Select(x => new OptionChain(normalized, x.Expiration, x.Timestamp, [], provider)))
            .OrderBy(x => x.Expiration).ThenBy(x => x.Timestamp).ToArray();
    }

    private static string Normalize(string symbol) => string.IsNullOrWhiteSpace(symbol)
        ? throw new ArgumentException("A symbol is required.", nameof(symbol)) : symbol.Trim().ToUpperInvariant();
}
