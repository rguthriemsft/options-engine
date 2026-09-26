using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using OptionsEngine.Application.Defense;
using OptionsEngine.Strategy.Defense;
using OptionsEngine.Strategy.EntryStrategy;
using OptionsEngine.Strategy.Indicators;

namespace OptionsEngine.Infrastructure.Persistence;

/// <summary>Persists Phase 6 analytical artifacts once and reconstructs them only from immutable JSON.</summary>
public sealed class SqliteDefenseEvaluationRepository(OptionsEngineDbContext db)
    : IDefenseEvaluationRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters =
        {
            new JsonStringEnumConverter(null, allowIntegerValues: false),
            new ConfigurationVersionJsonConverter(),
            new DefenseStrategyVersionJsonConverter(),
            new RollStrategyVersionJsonConverter(),
            new IndicatorCalculationVersionJsonConverter(),
            new StrategyVersionJsonConverter()
        }
    };

    public async Task InsertAsync(DefenseEvaluationBundle bundle,
        CancellationToken cancellationToken = default)
    {
        DefenseEvaluationPersistenceValidator.Validate(bundle);
        var defense = bundle.DefenseEvaluation;
        var roll = bundle.RollEvaluation;

        if (await db.DefenseEvaluations.AsNoTracking().AnyAsync(
                x => x.DefenseEvaluationId == defense.DefenseEvaluationId, cancellationToken))
            throw new InvalidOperationException(
                $"Defense evaluation '{defense.DefenseEvaluationId}' already exists and is immutable.");
        if (roll is not null && await db.RollEvaluations.AsNoTracking().AnyAsync(
                x => x.RollEvaluationId == roll.RollEvaluationId ||
                    x.DefenseEvaluationId == roll.DefenseEvaluationId, cancellationToken))
            throw new InvalidOperationException(
                $"Roll evaluation '{roll.RollEvaluationId}' or its DefenseEvaluation relationship already exists and is immutable.");

        var defenseEntity = MapDefense(defense);
        ValidateDefenseSummary(defenseEntity, defense);
        db.DefenseEvaluations.Add(defenseEntity);
        if (roll is not null)
        {
            var rollEntity = MapRoll(roll);
            ValidateRollSummary(rollEntity, roll);
            db.RollEvaluations.Add(rollEntity);
        }

        // EF Core wraps this multi-row SaveChanges call in a transaction, so Defense and Roll commit atomically.
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<DefenseEvaluation?> GetDefenseEvaluationByIdAsync(Guid defenseEvaluationId,
        CancellationToken cancellationToken = default)
    {
        var json = await db.DefenseEvaluations.AsNoTracking()
            .Where(x => x.DefenseEvaluationId == defenseEvaluationId)
            .Select(x => x.EvaluationJson)
            .SingleOrDefaultAsync(cancellationToken);
        return json is null ? null : Deserialize<DefenseEvaluation>(json, "defense");
    }

    public async Task<RollEvaluation?> GetRollEvaluationByIdAsync(Guid rollEvaluationId,
        CancellationToken cancellationToken = default)
    {
        var json = await db.RollEvaluations.AsNoTracking()
            .Where(x => x.RollEvaluationId == rollEvaluationId)
            .Select(x => x.EvaluationJson)
            .SingleOrDefaultAsync(cancellationToken);
        return json is null ? null : Deserialize<RollEvaluation>(json, "roll");
    }

    public async Task<IReadOnlyList<DefenseEvaluationHistoryItem>> GetDefenseEvaluationHistoryAsync(
        Guid holdingId, long openShortCallPositionId, CancellationToken cancellationToken = default)
    {
        var rows = await db.DefenseEvaluations.AsNoTracking()
            .Where(x => x.HoldingId == holdingId &&
                x.OpenShortCallPositionId == openShortCallPositionId)
            .ToListAsync(cancellationToken);
        return rows.OrderByDescending(x => x.CalculatedAtUtc)
            .ThenByDescending(x => x.DefenseEvaluationEntityId)
            .Select(x => new DefenseEvaluationHistoryItem(
                x.DefenseEvaluationId, x.OpenShortCallPositionId, x.HoldingId, x.Symbol,
                x.OptionSymbol, x.DefenseEvaluationTimestampUtc, x.CalculatedAtUtc, x.Drs,
                ParseNullable<DrsClassification>(x.DrsClassification),
                Enum.Parse<ProfitTakingSignal>(x.ProfitTakingSignal),
                Enum.Parse<HardDefenseStatus>(x.HardDefenseStatus), x.RollEngineRequired,
                Enum.Parse<DefenseDisposition>(x.Disposition), x.RollEvaluationId, x.CurrentCcos))
            .ToArray();
    }

    private static DefenseEvaluationEntity MapDefense(DefenseEvaluation value) => new()
    {
        DefenseEvaluationId = value.DefenseEvaluationId,
        OpenShortCallPositionId = value.OpenShortCallPositionId,
        HoldingId = value.HoldingId,
        Symbol = value.Symbol,
        OptionSymbol = value.OptionSymbol,
        DefenseEvaluationTimestampUtc = value.DefenseEvaluationTimestampUtc,
        CalculatedAtUtc = value.CalculatedAtUtc,
        Drs = value.Drs.Score,
        DrsClassification = value.Drs.Classification?.ToString(),
        ProfitTakingSignal = value.ProfitTaking.Signal.ToString(),
        HardDefenseStatus = value.HardDefenseStatus.ToString(),
        RollEngineRequired = value.RollEngineRequired,
        RollEvaluationId = value.RollEvaluationId,
        Disposition = value.Disposition.ToString(),
        CurrentCcos = value.CurrentCcos,
        ConfigurationVersion = value.ConfigurationVersion.Value,
        DefenseStrategyVersion = value.DefenseStrategyVersion.Value,
        RollStrategyVersion = value.RollStrategyVersion.Value,
        EvaluationJson = JsonSerializer.Serialize(value, JsonOptions)
    };

    private static RollEvaluationEntity MapRoll(RollEvaluation value) => new()
    {
        RollEvaluationId = value.RollEvaluationId,
        DefenseEvaluationId = value.DefenseEvaluationId,
        DefenseEvaluationTimestampUtc = value.DefenseEvaluationTimestampUtc,
        CalculatedAtUtc = value.CalculatedAtUtc,
        CurrentCcos = value.CurrentCcos,
        RankableCandidateCount = value.Candidates.Count(x =>
            x.State == RollCandidateEvaluationState.Rankable),
        InsufficientCandidateCount = value.Candidates.Count(x =>
            x.State == RollCandidateEvaluationState.InsufficientData),
        PreferredOptionSymbol = value.PreferredOptionSymbol,
        PreferredStrike = value.PreferredStrike,
        PreferredExpiration = value.PreferredExpiration,
        PreferredRqs = value.PreferredRqs,
        ConfigurationVersion = value.ConfigurationVersion.Value,
        RollStrategyVersion = value.StrategyVersion.Value,
        EvaluationJson = JsonSerializer.Serialize(value, JsonOptions)
    };

    private static void ValidateDefenseSummary(DefenseEvaluationEntity entity, DefenseEvaluation value)
    {
        if (entity.DefenseEvaluationId != value.DefenseEvaluationId ||
            entity.HoldingId != value.HoldingId ||
            entity.OpenShortCallPositionId != value.OpenShortCallPositionId ||
            entity.Disposition != value.Disposition.ToString() ||
            entity.RollEvaluationId != value.RollEvaluationId ||
            entity.ConfigurationVersion != value.ConfigurationVersion.Value)
            throw new InvalidOperationException("DefenseEvaluation relational summary contradicts its JSON artifact.");
    }

    private static void ValidateRollSummary(RollEvaluationEntity entity, RollEvaluation value)
    {
        if (entity.RollEvaluationId != value.RollEvaluationId ||
            entity.DefenseEvaluationId != value.DefenseEvaluationId ||
            entity.PreferredOptionSymbol != value.PreferredOptionSymbol ||
            entity.PreferredStrike != value.PreferredStrike ||
            entity.PreferredExpiration != value.PreferredExpiration ||
            entity.ConfigurationVersion != value.ConfigurationVersion.Value)
            throw new InvalidOperationException("RollEvaluation relational summary contradicts its JSON artifact.");
    }

    private static T Deserialize<T>(string json, string artifactName) =>
        JsonSerializer.Deserialize<T>(json, JsonOptions)
        ?? throw new InvalidOperationException($"Persisted {artifactName} evaluation payload is empty.");

    private static TEnum? ParseNullable<TEnum>(string? value) where TEnum : struct, Enum =>
        value is null ? null : Enum.Parse<TEnum>(value);

    private sealed class ConfigurationVersionJsonConverter : JsonConverter<ConfigurationVersion>
    {
        public override ConfigurationVersion Read(ref Utf8JsonReader reader, Type typeToConvert,
            JsonSerializerOptions options) => new(reader.GetInt32());
        public override void Write(Utf8JsonWriter writer, ConfigurationVersion value,
            JsonSerializerOptions options) => writer.WriteNumberValue(value.Value);
    }

    private sealed class DefenseStrategyVersionJsonConverter : JsonConverter<DefenseStrategyVersion>
    {
        public override DefenseStrategyVersion Read(ref Utf8JsonReader reader, Type typeToConvert,
            JsonSerializerOptions options) => new(reader.GetString()!);
        public override void Write(Utf8JsonWriter writer, DefenseStrategyVersion value,
            JsonSerializerOptions options) => writer.WriteStringValue(value.Value);
    }

    private sealed class RollStrategyVersionJsonConverter : JsonConverter<RollStrategyVersion>
    {
        public override RollStrategyVersion Read(ref Utf8JsonReader reader, Type typeToConvert,
            JsonSerializerOptions options) => new(reader.GetString()!);
        public override void Write(Utf8JsonWriter writer, RollStrategyVersion value,
            JsonSerializerOptions options) => writer.WriteStringValue(value.Value);
    }

    private sealed class IndicatorCalculationVersionJsonConverter : JsonConverter<IndicatorCalculationVersion>
    {
        public override IndicatorCalculationVersion Read(ref Utf8JsonReader reader, Type typeToConvert,
            JsonSerializerOptions options) => new(reader.GetString()!);
        public override void Write(Utf8JsonWriter writer, IndicatorCalculationVersion value,
            JsonSerializerOptions options) => writer.WriteStringValue(value.Value);
    }

    private sealed class StrategyVersionJsonConverter : JsonConverter<StrategyVersion>
    {
        public override StrategyVersion Read(ref Utf8JsonReader reader, Type typeToConvert,
            JsonSerializerOptions options) => new(reader.GetString()!);
        public override void Write(Utf8JsonWriter writer, StrategyVersion value,
            JsonSerializerOptions options) => writer.WriteStringValue(value.Value);
    }
}
