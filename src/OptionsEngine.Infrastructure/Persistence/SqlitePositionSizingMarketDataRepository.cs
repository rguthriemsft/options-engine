using Microsoft.EntityFrameworkCore;
using OptionsEngine.Application.PositionSizing;

namespace OptionsEngine.Infrastructure.Persistence;

/// <summary>Read-only Phase 5 historical market-data queries over existing normalized tables.</summary>
public sealed class SqlitePositionSizingMarketDataRepository(OptionsEngineDbContext db) : IPositionSizingMarketDataRepository
{
    public async Task<PositionSizingDailyClose?> GetLatestDailyCloseAsync(string symbol, string provider,
        DateOnly indicatorAsOfDate, CancellationToken cancellationToken = default)
    {
        var normalizedSymbol = Normalize(symbol, nameof(symbol));
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        var row = await db.HistoricalPriceBars.AsNoTracking()
            .Where(x => x.Symbol == normalizedSymbol && x.Provider == provider && x.Date <= indicatorAsOfDate)
            .OrderByDescending(x => x.Date).ThenByDescending(x => x.HistoricalPriceBarId)
            .FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : new PositionSizingDailyClose(row.Close, row.Date);
    }

    public async Task<IReadOnlyList<PositionSizingOptionDeltaObservation>> GetLatestOptionDeltaObservationsAsync(
        IReadOnlyCollection<string> optionSymbols,
        string provider,
        DateTimeOffset sizingTimestampUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(optionSymbols);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        if (sizingTimestampUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Sizing timestamp must be UTC.", nameof(sizingTimestampUtc));
        var normalizedSymbols = optionSymbols.Select(symbol => Normalize(symbol, nameof(optionSymbols)))
            .Distinct(StringComparer.Ordinal).ToArray();
        if (normalizedSymbols.Length == 0)
            return [];

        var rows = await db.OptionContractSnapshots.AsNoTracking()
            .Where(x => normalizedSymbols.Contains(x.OptionSymbol) && x.Provider == provider &&
                x.TimestampUtcTicks <= sizingTimestampUtc.UtcTicks)
            .OrderBy(x => x.OptionSymbol).ThenByDescending(x => x.TimestampUtcTicks)
            .ThenByDescending(x => x.OptionContractSnapshotId)
            .ToListAsync(cancellationToken);
        var observations = new List<PositionSizingOptionDeltaObservation>();
        foreach (var group in rows.GroupBy(row => row.OptionSymbol, StringComparer.Ordinal))
        {
            var latestTimestamp = group.Max(row => row.TimestampUtcTicks);
            var latest = group.Where(row => row.TimestampUtcTicks == latestTimestamp).ToArray();
            if (latest.Length != 1)
                throw new InvalidOperationException(
                    $"Multiple persisted option observations share the latest timestamp for {group.Key}.");
            observations.Add(new PositionSizingOptionDeltaObservation(
                latest[0].OptionSymbol, latest[0].Delta, latest[0].Timestamp));
        }

        return observations;
    }

    private static string Normalize(string value, string parameterName) => string.IsNullOrWhiteSpace(value)
        ? throw new ArgumentException("A symbol is required.", parameterName)
        : value.Trim().ToUpperInvariant();
}
