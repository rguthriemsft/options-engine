using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using OptionsEngine.Application.EntryStrategy;
using OptionsEngine.Strategy.EntryStrategy;
using OptionsEngine.Strategy.Indicators;

namespace OptionsEngine.Infrastructure.Persistence;

/// <summary>Persists Phase 4 evaluations as append-only rows; retrieval never consults mutable source tables.</summary>
public sealed class SqliteEntryStrategyEvaluationRepository(OptionsEngineDbContext db) : IEntryStrategyEvaluationRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters =
        {
            new JsonStringEnumConverter(null, allowIntegerValues: false),
            new ConfigurationVersionJsonConverter()
        }
    };

    public async Task InsertAsync(PersistedEntryStrategyEvaluation evaluation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evaluation);
        var value = evaluation.Evaluation;
        if (value.EntryStrategyEvaluationId == Guid.Empty)
            throw new ArgumentException("An evaluation identifier is required.", nameof(evaluation));
        if (value.CalculatedAtUtc.Offset != TimeSpan.Zero || value.Context.EvaluationTimestampUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Evaluation timestamps must be UTC.", nameof(evaluation));
        value.Context.Validate();
        if (await db.EntryStrategyEvaluations.AsNoTracking()
            .AnyAsync(x => x.EntryStrategyEvaluationId == value.EntryStrategyEvaluationId, cancellationToken))
            throw new InvalidOperationException($"Entry strategy evaluation '{value.EntryStrategyEvaluationId}' already exists and is immutable.");
        var entity = new EntryStrategyEvaluationEntity
        {
            EntryStrategyEvaluationId = value.EntryStrategyEvaluationId,
            HoldingId = value.Context.Holding.HoldingId,
            Symbol = value.Context.Holding.Symbol,
            IndicatorAsOfDate = value.Context.IndicatorAsOfDate,
            EvaluationTimestampUtc = value.Context.EvaluationTimestampUtc,
            CalculatedAtUtc = value.CalculatedAtUtc,
            IndicatorCalculationVersion = value.Context.Indicators.IndicatorCalculationVersion.Value,
            ConfigurationVersion = value.Context.Configuration.Version.Value,
            StrategyVersion = value.Context.StrategyVersion.Value,
            CcosStatus = (value.Ccos?.Status ?? ScoreStatus.Unavailable).ToString(),
            CcosScore = value.Ccos?.Score,
            CcosClassification = value.Ccos?.Classification,
            EntryCandidateExists = value.EntryCandidateExists,
            PreferredInitialOptionSymbol = value.PreferredInitialOptionSymbol,
            PreferredInitialStrike = value.PreferredInitialStrike,
            PreferredInitialExpiration = value.PreferredInitialExpiration,
            PreferredInitialReferencePremium = value.PreferredInitialReferencePremium,
            DispositionReason = value.DispositionReason.ToString(),
            EvaluationJson = JsonSerializer.Serialize(evaluation, JsonOptions)
        };
        db.EntryStrategyEvaluations.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<PersistedEntryStrategyEvaluation?> GetByIdAsync(Guid evaluationId,
        CancellationToken cancellationToken = default)
    {
        var entity = await db.EntryStrategyEvaluations.AsNoTracking()
            .SingleOrDefaultAsync(x => x.EntryStrategyEvaluationId == evaluationId, cancellationToken);
        return entity is null ? null : Deserialize(entity.EvaluationJson);
    }

    public async Task<IReadOnlyList<EntryStrategyEvaluationHistoryItem>> GetHistoryByHoldingAsync(Guid holdingId,
        CancellationToken cancellationToken = default)
    {
        var rows = await db.EntryStrategyEvaluations.AsNoTracking()
            .Where(x => x.HoldingId == holdingId)
            .ToListAsync(cancellationToken);
        return rows.OrderByDescending(x => x.CalculatedAtUtc).Select(x => new EntryStrategyEvaluationHistoryItem(
            x.EntryStrategyEvaluationId, x.HoldingId, x.Symbol, x.IndicatorAsOfDate, x.EvaluationTimestampUtc,
            x.CalculatedAtUtc, Enum.Parse<ScoreStatus>(x.CcosStatus), x.CcosScore, x.CcosClassification,
            x.EntryCandidateExists, x.PreferredInitialOptionSymbol, x.PreferredInitialStrike,
            x.PreferredInitialExpiration, Enum.Parse<DispositionReasonCode>(x.DispositionReason),
            x.IndicatorCalculationVersion, new ConfigurationVersion(x.ConfigurationVersion), x.StrategyVersion)).ToArray();
    }

    private static PersistedEntryStrategyEvaluation Deserialize(string json) =>
        JsonSerializer.Deserialize<PersistedEntryStrategyEvaluation>(json, JsonOptions)
        ?? throw new InvalidOperationException("Persisted entry-strategy evaluation payload is empty.");

    private sealed class ConfigurationVersionJsonConverter : JsonConverter<ConfigurationVersion>
    {
        public override ConfigurationVersion Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            new(reader.GetInt32());

        public override void Write(Utf8JsonWriter writer, ConfigurationVersion value, JsonSerializerOptions options) =>
            writer.WriteNumberValue(value.Value);
    }
}
