using System.Collections.Immutable;
using Microsoft.EntityFrameworkCore;
using OptionsEngine.Application.Defense;
using OptionsEngine.MarketData.Models;
using OptionsEngine.Strategy.Defense;
using OptionsEngine.Strategy.EntryStrategy;

namespace OptionsEngine.Infrastructure.Persistence;

/// <summary>Deterministic Phase 6 reads over persisted provider-independent option observations.</summary>
public sealed class SqliteDefenseMarketDataRepository(OptionsEngineDbContext db) : IDefenseMarketDataRepository
{
    private static readonly TimeZoneInfo NewYork =
        TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    public async Task<DefenseOptionObservation?> GetLatestOptionObservationAsync(string optionSymbol,
        string provider, DateTimeOffset evaluationTimestampUtc,
        CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(optionSymbol, nameof(optionSymbol));
        ValidateProviderAndCutoff(provider, evaluationTimestampUtc);
        var row = await db.OptionContractSnapshots.AsNoTracking()
            .Where(value => value.OptionSymbol == normalized && value.Provider == provider &&
                            value.TimestampUtcTicks <= evaluationTimestampUtc.UtcTicks)
            .OrderByDescending(value => value.TimestampUtcTicks)
            .ThenByDescending(value => value.OptionContractSnapshotId)
            .FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : Map(row);
    }

    public async Task<DefenseOptionObservation?> GetPreviousTradingDateOptionObservationAsync(
        string optionSymbol, string provider, DateTimeOffset currentObservationTimestampUtc,
        DateTimeOffset evaluationTimestampUtc, CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(optionSymbol, nameof(optionSymbol));
        ValidateProviderAndCutoff(provider, evaluationTimestampUtc);
        if (currentObservationTimestampUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Current observation timestamp must be UTC.",
                nameof(currentObservationTimestampUtc));
        if (currentObservationTimestampUtc > evaluationTimestampUtc)
            throw new ArgumentException("Current observation cannot be later than the evaluation cutoff.",
                nameof(currentObservationTimestampUtc));

        var rows = await db.OptionContractSnapshots.AsNoTracking()
            .Where(value => value.OptionSymbol == normalized && value.Provider == provider &&
                            value.TimestampUtcTicks < currentObservationTimestampUtc.UtcTicks &&
                            value.TimestampUtcTicks <= evaluationTimestampUtc.UtcTicks)
            .OrderByDescending(value => value.TimestampUtcTicks)
            .ThenByDescending(value => value.OptionContractSnapshotId)
            .ToListAsync(cancellationToken);
        var currentTradingDate = TradingDate(currentObservationTimestampUtc);
        var previous = rows.FirstOrDefault(value => TradingDate(value.Timestamp) < currentTradingDate);
        return previous is null ? null : Map(previous);
    }

    public async Task<IReadOnlyList<SelectedRollChainSnapshot>> GetLatestOptionChainsAsync(
        string underlyingSymbol, string provider, DateOnly minimumExpiration,
        DateOnly maximumExpiration, DateTimeOffset evaluationTimestampUtc,
        CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(underlyingSymbol, nameof(underlyingSymbol));
        ValidateProviderAndCutoff(provider, evaluationTimestampUtc);
        if (minimumExpiration > maximumExpiration)
            throw new ArgumentException("Minimum expiration must not be after maximum expiration.",
                nameof(minimumExpiration));

        var cutoffTicks = evaluationTimestampUtc.UtcTicks;
        var rows = await db.OptionContractSnapshots.AsNoTracking()
            .Where(value => value.UnderlyingSymbol == normalized && value.Provider == provider &&
                            value.Expiration >= minimumExpiration && value.Expiration <= maximumExpiration &&
                            value.TimestampUtcTicks <= cutoffTicks)
            .OrderBy(value => value.Expiration).ThenBy(value => value.TimestampUtcTicks)
            .ThenBy(value => value.OptionSymbol).ThenBy(value => value.OptionContractSnapshotId)
            .ToListAsync(cancellationToken);
        var emptyRows = await db.EmptyOptionChainSnapshots.AsNoTracking()
            .Where(value => value.UnderlyingSymbol == normalized && value.Provider == provider &&
                            value.Expiration >= minimumExpiration && value.Expiration <= maximumExpiration &&
                            value.TimestampUtcTicks <= cutoffTicks)
            .OrderBy(value => value.Expiration).ThenBy(value => value.TimestampUtcTicks)
            .ThenBy(value => value.EmptyOptionChainSnapshotId)
            .ToListAsync(cancellationToken);

        var expirations = rows.Select(value => value.Expiration)
            .Concat(emptyRows.Select(value => value.Expiration)).Distinct().Order().ToArray();
        var selected = new List<SelectedRollChainSnapshot>(expirations.Length);
        foreach (var expiration in expirations)
        {
            var populated = rows.Where(value => value.Expiration == expiration).ToArray();
            var empty = emptyRows.Where(value => value.Expiration == expiration).ToArray();
            var populatedTicks = populated.Length == 0 ? (long?)null : populated.Max(value => value.TimestampUtcTicks);
            var emptyTicks = empty.Length == 0 ? (long?)null : empty.Max(value => value.TimestampUtcTicks);
            if (populatedTicks is not null && populatedTicks == emptyTicks)
                throw new InvalidOperationException(
                    $"Both populated and empty chain snapshots exist for {expiration:yyyy-MM-dd} at the same timestamp.");
            if (emptyTicks is not null &&
                (populatedTicks is null || emptyTicks.Value > populatedTicks.Value))
            {
                var emptySnapshot = empty.Where(value => value.TimestampUtcTicks == emptyTicks)
                    .OrderByDescending(value => value.EmptyOptionChainSnapshotId).First();
                selected.Add(new SelectedRollChainSnapshot(normalized, expiration,
                    emptySnapshot.Timestamp, provider, []));
                continue;
            }

            var contracts = populated.Where(value => value.TimestampUtcTicks == populatedTicks)
                .GroupBy(value => value.OptionSymbol, StringComparer.Ordinal)
                .Select(group => group.OrderByDescending(value => value.OptionContractSnapshotId).First())
                .OrderBy(value => value.OptionSymbol, StringComparer.Ordinal).Select(Map).ToImmutableArray();
            selected.Add(new SelectedRollChainSnapshot(normalized, expiration,
                populated.First(value => value.TimestampUtcTicks == populatedTicks).Timestamp,
                provider, contracts));
        }
        return selected;
    }

    private static DefenseOptionObservation Map(OptionContractSnapshotEntity value) => new(
        value.OptionSymbol, value.UnderlyingSymbol, value.Timestamp, value.Expiration, value.Strike,
        value.OptionType == OptionType.Call ? OptionContractType.Call : OptionContractType.Put,
        value.Bid, value.Ask, value.Last, value.OpenInterest, value.ImpliedVolatility, value.Delta,
        value.Gamma, value.Theta, value.Vega, value.UnderlyingPrice, value.Provider);

    private static DateOnly TradingDate(DateTimeOffset timestampUtc) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(timestampUtc, NewYork).DateTime);

    private static string Normalize(string value, string parameterName) => string.IsNullOrWhiteSpace(value)
        ? throw new ArgumentException("A symbol is required.", parameterName)
        : value.Trim().ToUpperInvariant();

    private static void ValidateProviderAndCutoff(string provider, DateTimeOffset cutoff)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        if (cutoff.Offset != TimeSpan.Zero)
            throw new ArgumentException("Evaluation timestamp must be UTC.", nameof(cutoff));
    }
}
