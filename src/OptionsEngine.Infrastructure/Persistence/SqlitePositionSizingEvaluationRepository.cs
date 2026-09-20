using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using OptionsEngine.Application.PositionSizing;
using OptionsEngine.Strategy.Indicators;
using OptionsEngine.Strategy.PositionSizing;

namespace OptionsEngine.Infrastructure.Persistence;

/// <summary>Persists complete Position Sizing evaluations as immutable append-only rows.</summary>
public sealed class SqlitePositionSizingEvaluationRepository(OptionsEngineDbContext db) : IPositionSizingEvaluationRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters =
        {
            new JsonStringEnumConverter(null, allowIntegerValues: false),
            new ConfigurationVersionJsonConverter()
        }
    };

    public async Task InsertAsync(PositionSizingEvaluation evaluation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evaluation);
        if (evaluation.PositionSizingEvaluationId == Guid.Empty)
            throw new ArgumentException("A Position Sizing evaluation identifier is required.", nameof(evaluation));
        if (evaluation.EntryStrategyEvaluationId == Guid.Empty)
            throw new ArgumentException("A source entry strategy evaluation identifier is required.", nameof(evaluation));
        if (evaluation.CalculatedAtUtc.Offset != TimeSpan.Zero || evaluation.SizingTimestampUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Evaluation timestamps must be UTC.", nameof(evaluation));
        ArgumentNullException.ThrowIfNull(evaluation.ResolvedConfiguration);
        ArgumentNullException.ThrowIfNull(evaluation.StrategyVersion);
        ArgumentNullException.ThrowIfNull(evaluation.Result);
        evaluation.ResolvedConfiguration.Validate();
        evaluation.HoldingContext?.Validate();

        if (await db.PositionSizingEvaluations.AsNoTracking().AnyAsync(
                x => x.PositionSizingEvaluationId == evaluation.PositionSizingEvaluationId, cancellationToken))
            throw new InvalidOperationException(
                $"Position Sizing evaluation '{evaluation.PositionSizingEvaluationId}' already exists and is immutable.");

        var result = evaluation.Result;
        db.PositionSizingEvaluations.Add(new PositionSizingEvaluationEntity
        {
            PositionSizingEvaluationId = evaluation.PositionSizingEvaluationId,
            EntryStrategyEvaluationId = evaluation.EntryStrategyEvaluationId,
            HoldingId = evaluation.HoldingContext?.HoldingId,
            Symbol = evaluation.HoldingContext?.Symbol,
            CalculatedAtUtc = evaluation.CalculatedAtUtc,
            SizingTimestampUtc = evaluation.SizingTimestampUtc,
            Status = result.Status.ToString(),
            ConfigurationVersion = evaluation.ConfigurationVersion.Value,
            PositionSizingStrategyVersion = evaluation.StrategyVersion.Value,
            DesiredTotalContracts = result.DesiredTotalContracts,
            AdditionalContracts = result.AdditionalContracts,
            ResultingTotalContracts = result.ResultingTotalContracts,
            ExistingDer = result.ExistingDer,
            MaximumDer = result.MaximumDer,
            EvaluationJson = JsonSerializer.Serialize(evaluation, JsonOptions)
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<PositionSizingEvaluation?> GetByIdAsync(Guid positionSizingEvaluationId,
        CancellationToken cancellationToken = default)
    {
        var entity = await db.PositionSizingEvaluations.AsNoTracking().SingleOrDefaultAsync(
            x => x.PositionSizingEvaluationId == positionSizingEvaluationId, cancellationToken);
        return entity is null ? null : JsonSerializer.Deserialize<PositionSizingEvaluation>(entity.EvaluationJson, JsonOptions)
            ?? throw new InvalidOperationException("Persisted Position Sizing evaluation payload is empty.");
    }

    private sealed class ConfigurationVersionJsonConverter : JsonConverter<ConfigurationVersion>
    {
        public override ConfigurationVersion Read(ref Utf8JsonReader reader, Type typeToConvert,
            JsonSerializerOptions options) => new(reader.GetInt32());

        public override void Write(Utf8JsonWriter writer, ConfigurationVersion value,
            JsonSerializerOptions options) => writer.WriteNumberValue(value.Value);
    }
}
