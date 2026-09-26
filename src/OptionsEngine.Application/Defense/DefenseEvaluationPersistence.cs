using OptionsEngine.Strategy.Defense;

namespace OptionsEngine.Application.Defense;

/// <summary>Lightweight relational projection for position-scoped Phase 6 history.</summary>
public sealed record DefenseEvaluationHistoryItem(
    Guid DefenseEvaluationId,
    long OpenShortCallPositionId,
    Guid HoldingId,
    string Symbol,
    string OptionSymbol,
    DateTimeOffset DefenseEvaluationTimestampUtc,
    DateTimeOffset CalculatedAtUtc,
    double? Drs,
    DrsClassification? DrsClassification,
    ProfitTakingSignal ProfitTakingSignal,
    HardDefenseStatus HardDefenseStatus,
    bool RollEngineRequired,
    DefenseDisposition Disposition,
    Guid? RollEvaluationId,
    double? CurrentCcos);

/// <summary>Append-only persistence and passive read boundary for immutable Phase 6 artifacts.</summary>
public interface IDefenseEvaluationRepository
{
    Task InsertAsync(DefenseEvaluationBundle bundle, CancellationToken cancellationToken = default);
    Task<DefenseEvaluation?> GetDefenseEvaluationByIdAsync(Guid defenseEvaluationId,
        CancellationToken cancellationToken = default);
    Task<RollEvaluation?> GetRollEvaluationByIdAsync(Guid rollEvaluationId,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DefenseEvaluationHistoryItem>> GetDefenseEvaluationHistoryAsync(
        Guid holdingId, long openShortCallPositionId, CancellationToken cancellationToken = default);
}

/// <summary>Application write boundary for a complete Phase 6E bundle.</summary>
public interface IDefenseEvaluationWriter
{
    Task<DefenseEvaluationBundle> PersistAsync(DefenseEvaluationBundle bundle,
        CancellationToken cancellationToken = default);
}

/// <summary>Persists the exact Phase 6E artifact without rereading or recalculating mutable state.</summary>
public sealed class DefenseEvaluationPersistenceService(IDefenseEvaluationRepository repository)
    : IDefenseEvaluationWriter
{
    public async Task<DefenseEvaluationBundle> PersistAsync(DefenseEvaluationBundle bundle,
        CancellationToken cancellationToken = default)
    {
        DefenseEvaluationPersistenceValidator.Validate(bundle);
        await repository.InsertAsync(bundle, cancellationToken);
        return bundle;
    }
}

public static class DefenseEvaluationPersistenceValidator
{
    public static void Validate(DefenseEvaluationBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        var defense = bundle.DefenseEvaluation
            ?? throw new ArgumentException("A DefenseEvaluation is required.", nameof(bundle));
        if (defense.DefenseEvaluationId == Guid.Empty)
            throw new ArgumentException("A DefenseEvaluation identifier is required.", nameof(bundle));
        if (defense.DefenseEvaluationTimestampUtc.Offset != TimeSpan.Zero ||
            defense.CalculatedAtUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Defense evaluation timestamps must be UTC.", nameof(bundle));
        defense.PositionSnapshot.Validate();
        defense.HoldingContext.Validate();
        defense.ResolvedDefenseConfiguration.Validate();
        defense.ResolvedRollConfiguration.Validate();
        if (defense.OpenShortCallPositionId != defense.PositionSnapshot.OpenShortCallPositionId ||
            defense.HoldingId != defense.PositionSnapshot.HoldingId ||
            defense.HoldingId != defense.HoldingContext.HoldingId ||
            !string.Equals(defense.Symbol, defense.HoldingContext.Symbol, StringComparison.Ordinal) ||
            !string.Equals(defense.OptionSymbol, defense.PositionSnapshot.OptionSymbol, StringComparison.Ordinal))
            throw new ArgumentException("DefenseEvaluation identity fields must match its immutable snapshots.", nameof(bundle));
        if (defense.ConfigurationVersion != defense.ResolvedDefenseConfiguration.Version ||
            defense.ConfigurationVersion != defense.ResolvedRollConfiguration.Version)
            throw new ArgumentException("DefenseEvaluation configuration identities are inconsistent.", nameof(bundle));
        if (defense.CurrentCcosContext?.Score != defense.CurrentCcos)
            throw new ArgumentException("DefenseEvaluation CurrentCCOS must match its preserved context.", nameof(bundle));

        if (!defense.RollEngineRequired)
        {
            if (defense.RollEvaluationId is not null || bundle.RollEvaluation is not null)
                throw new ArgumentException("A non-activated Roll Engine cannot have a RollEvaluation.", nameof(bundle));
            return;
        }

        var roll = bundle.RollEvaluation
            ?? throw new ArgumentException("An activated Roll Engine requires a RollEvaluation.", nameof(bundle));
        if (defense.RollEvaluationId is null || defense.RollEvaluationId == Guid.Empty ||
            defense.RollEvaluationId != roll.RollEvaluationId)
            throw new ArgumentException("DefenseEvaluation must reference the supplied RollEvaluation.", nameof(bundle));
        if (roll.RollEvaluationId == Guid.Empty || roll.DefenseEvaluationId != defense.DefenseEvaluationId)
            throw new ArgumentException("RollEvaluation must reference the supplied DefenseEvaluation.", nameof(bundle));
        if (roll.DefenseEvaluationTimestampUtc.Offset != TimeSpan.Zero || roll.CalculatedAtUtc.Offset != TimeSpan.Zero ||
            roll.DefenseEvaluationTimestampUtc != defense.DefenseEvaluationTimestampUtc ||
            roll.CalculatedAtUtc != defense.CalculatedAtUtc)
            throw new ArgumentException("Defense and Roll evaluation timestamps must match and be UTC.", nameof(bundle));
        roll.CurrentPositionSnapshot.Validate();
        roll.ResolvedConfiguration.Validate();
        if (roll.CurrentPositionSnapshot != defense.PositionSnapshot)
            throw new ArgumentException("RollEvaluation must preserve the DefenseEvaluation position snapshot.", nameof(bundle));
        if (roll.ConfigurationVersion != defense.ConfigurationVersion ||
            roll.ConfigurationVersion != roll.ResolvedConfiguration.Version ||
            roll.StrategyVersion != defense.RollStrategyVersion ||
            roll.ResolvedConfiguration != defense.ResolvedRollConfiguration)
            throw new ArgumentException("RollEvaluation configuration or strategy identities are inconsistent.", nameof(bundle));
        if (roll.CurrentCcosContext != defense.CurrentCcosContext ||
            roll.CurrentCcos != defense.CurrentCcos || roll.CurrentCcosContext?.Score != roll.CurrentCcos)
            throw new ArgumentException("Defense and Roll CurrentCCOS contexts must match.", nameof(bundle));
        foreach (var chain in roll.SelectedChainSnapshots)
            chain.Validate();
    }
}
