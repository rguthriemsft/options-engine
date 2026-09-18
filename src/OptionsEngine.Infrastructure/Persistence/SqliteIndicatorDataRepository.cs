using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using OptionsEngine.Application.Indicators;
using OptionsEngine.MarketData.Models;
using OptionsEngine.Strategy.Indicators;

namespace OptionsEngine.Infrastructure.Persistence;

/// <summary>Reads persisted normalized Phase 2 observations and upserts canonical Phase 3 results.</summary>
public sealed class SqliteIndicatorDataRepository(OptionsEngineDbContext db) : IIndicatorDataRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new() { Converters = { new ConfigurationVersionJsonConverter() } };

    public async Task<IReadOnlyList<IndicatorPriceObservation>> GetPricesThroughAsync(string symbol, string provider,
        DateOnly asOfDate, CancellationToken cancellationToken = default)
    {
        symbol = Normalize(symbol);
        var rows = await db.HistoricalPriceBars.AsNoTracking()
            .Where(x => x.Symbol == symbol && x.Provider == provider && x.Date <= asOfDate)
            .OrderBy(x => x.Date).ToListAsync(cancellationToken);
        return rows.Select(x => new IndicatorPriceObservation(x.Symbol, x.Date, x.Open, x.High, x.Low, x.Close, x.Volume)).ToArray();
    }

    public async Task<IReadOnlyList<OptionChain>> GetOptionChainsThroughAsync(string symbol, string provider,
        DateOnly asOfDate, DateTimeOffset calculatedAt, CancellationToken cancellationToken = default)
    {
        symbol = Normalize(symbol);
        if (calculatedAt.Offset != TimeSpan.Zero) throw new ArgumentException("CalculatedAt must be UTC.", nameof(calculatedAt));
        // Phase 3C defines eligibility by UTC observation date and the calculation timestamp.
        var endOfAsOfUtcTicks = new DateTimeOffset(asOfDate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc)).UtcTicks;
        var latestEligibleTicks = Math.Min(endOfAsOfUtcTicks, calculatedAt.UtcTicks);
        var rows = await db.OptionContractSnapshots.AsNoTracking()
            .Where(x => x.UnderlyingSymbol == symbol && x.Provider == provider && x.Expiration > asOfDate &&
                x.TimestampUtcTicks <= latestEligibleTicks)
            .OrderBy(x => x.Expiration).ThenBy(x => x.TimestampUtcTicks).ThenBy(x => x.OptionSymbol)
            .ToListAsync(cancellationToken);
        var emptyRows = await db.EmptyOptionChainSnapshots.AsNoTracking()
            .Where(x => x.UnderlyingSymbol == symbol && x.Provider == provider && x.Expiration > asOfDate &&
                x.TimestampUtcTicks <= latestEligibleTicks)
            .OrderBy(x => x.Expiration).ThenBy(x => x.TimestampUtcTicks).ToListAsync(cancellationToken);
        var chains = rows.GroupBy(x => new { x.Expiration, x.TimestampUtcTicks })
            .Select(group => new OptionChain(symbol, group.Key.Expiration, group.First().Timestamp,
                group.Select(x => new OptionContractSnapshot(x.OptionSymbol, x.UnderlyingSymbol, x.Timestamp, x.Expiration,
                    x.Strike, x.OptionType, x.Bid, x.Ask, x.Last, x.Volume, x.OpenInterest, x.ImpliedVolatility,
                    x.Delta, x.Gamma, x.Theta, x.Vega, x.UnderlyingPrice, x.Provider)).ToArray(), provider))
            .Concat(emptyRows.Select(x => new OptionChain(symbol, x.Expiration, x.Timestamp, [], provider)))
            .OrderBy(x => x.Expiration).ThenBy(x => x.Timestamp).ToArray();
        return chains;
    }

    public async Task<IReadOnlyList<HistoricalIv30Observation>> GetPriorValidIv30Async(string symbol, DateOnly asOfDate,
        IndicatorCalculationVersion calculationVersion, ConfigurationVersion configurationVersion, int limit,
        CancellationToken cancellationToken = default)
    {
        symbol = Normalize(symbol);
        if (limit < 0) throw new ArgumentOutOfRangeException(nameof(limit));
        if (limit == 0) return [];
        var rows = await db.IndicatorSnapshots.AsNoTracking()
            .Where(x => x.Symbol == symbol && x.AsOfDate < asOfDate &&
                x.IndicatorCalculationVersion == calculationVersion.Value && x.ConfigurationVersion == configurationVersion.Value &&
                x.Iv30Value != null)
            .OrderByDescending(x => x.AsOfDate).Take(limit).ToListAsync(cancellationToken);
        return rows.Select(x => new HistoricalIv30Observation(x.Symbol, x.AsOfDate,
            IndicatorValue<double>.Available(x.Iv30Value!.Value), calculationVersion, configurationVersion)).ToArray();
    }

    public async Task<IndicatorSnapshot?> GetSnapshotAsync(string symbol, DateOnly asOfDate,
        IndicatorCalculationVersion calculationVersion, ConfigurationVersion configurationVersion,
        CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(symbol);
        var row = await db.IndicatorSnapshots.AsNoTracking().SingleOrDefaultAsync(x => x.Symbol == normalized && x.AsOfDate == asOfDate &&
            x.IndicatorCalculationVersion == calculationVersion.Value && x.ConfigurationVersion == configurationVersion.Value, cancellationToken);
        return row is null ? null : JsonSerializer.Deserialize<IndicatorSnapshot>(row.SnapshotJson, JsonOptions)
            ?? throw new InvalidOperationException("Stored indicator snapshot is empty.");
    }

    public async Task UpsertSnapshotAsync(IndicatorSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.CalculatedAt.Offset != TimeSpan.Zero) throw new ArgumentException("CalculatedAt must be UTC.", nameof(snapshot));
        if (snapshot.ConfigurationVersion.Value < 1) throw new ArgumentException("ConfigurationVersion must be positive.", nameof(snapshot));
        var normalized = snapshot with { Symbol = Normalize(snapshot.Symbol) };
        var json = JsonSerializer.Serialize(normalized, JsonOptions);
        var iv30 = normalized.Iv30.Status == IndicatorValueStatus.Available ? normalized.Iv30.Value : null;
        if (iv30 is { } value && (!double.IsFinite(value) || value < 0))
            throw new ArgumentException("Available IV30 must be a finite non-negative value.", nameof(snapshot));

        // One SQLite statement makes insert-or-replace atomic; the unique index protects concurrent retries.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO IndicatorSnapshots (Symbol, AsOfDate, IndicatorCalculationVersion, ConfigurationVersion, CalculatedAt, Iv30Value, SnapshotJson)
            VALUES ({normalized.Symbol}, {normalized.AsOfDate}, {normalized.IndicatorCalculationVersion.Value},
                    {normalized.ConfigurationVersion.Value}, {normalized.CalculatedAt}, {iv30}, {json})
            ON CONFLICT(Symbol, AsOfDate, IndicatorCalculationVersion, ConfigurationVersion)
            DO UPDATE SET CalculatedAt = excluded.CalculatedAt, Iv30Value = excluded.Iv30Value, SnapshotJson = excluded.SnapshotJson;
            """, cancellationToken);
    }

    private static string Normalize(string symbol) => string.IsNullOrWhiteSpace(symbol)
        ? throw new ArgumentException("A symbol is required.", nameof(symbol)) : symbol.Trim().ToUpperInvariant();

    private sealed class ConfigurationVersionJsonConverter : JsonConverter<ConfigurationVersion>
    {
        public override ConfigurationVersion Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            new(reader.GetInt32());

        public override void Write(Utf8JsonWriter writer, ConfigurationVersion value, JsonSerializerOptions options) =>
            writer.WriteNumberValue(value.Value);
    }
}
