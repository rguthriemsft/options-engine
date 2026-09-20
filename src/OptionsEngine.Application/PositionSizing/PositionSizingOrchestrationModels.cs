using OptionsEngine.Domain.Accounts;
using OptionsEngine.Application.EntryStrategy;
using OptionsEngine.Strategy.PositionSizing;

namespace OptionsEngine.Application.PositionSizing;

/// <summary>Read-only account Holding lookup needed to assemble Phase 5 concentration inputs.</summary>
public interface IPositionSizingHoldingRepository
{
    Task<IReadOnlyList<Holding>> GetByAccountIdAsync(Guid accountId, CancellationToken cancellationToken = default);
}

/// <summary>Read-only historical market-data boundary for Phase 5 sizing assembly.</summary>
public interface IPositionSizingMarketDataRepository
{
    Task<PositionSizingDailyClose?> GetLatestDailyCloseAsync(
        string symbol,
        string provider,
        DateOnly indicatorAsOfDate,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PositionSizingOptionDeltaObservation>> GetLatestOptionDeltaObservationsAsync(
        IReadOnlyCollection<string> optionSymbols,
        string provider,
        DateTimeOffset sizingTimestampUtc,
        CancellationToken cancellationToken = default);
}

public sealed record PositionSizingDailyClose(decimal Close, DateOnly Date);

public sealed record PositionSizingOptionDeltaObservation(
    string OptionSymbol,
    double? Delta,
    DateTimeOffset ObservationTimestampUtc);

/// <summary>Current-state boundary only; Phase 5E adds no open-call persistence model.</summary>
public interface IOpenShortCallPositionRepository
{
    Task<IReadOnlyList<ExistingShortCallExposure>> GetOpenShortCallsByHoldingAsync(
        Guid holdingId,
        CancellationToken cancellationToken = default);
}

/// <summary>All server-owned Position Sizing configuration resolved for one evaluation.</summary>
public sealed record PositionSizingOrchestrationConfiguration
{
    public required PositionSizingConfiguration PositionSizingConfiguration { get; init; }
    public required PositionSizingStrategyVersion StrategyVersion { get; init; }

    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(PositionSizingConfiguration);
        ArgumentNullException.ThrowIfNull(StrategyVersion);
        PositionSizingConfiguration.Validate();
    }
}

/// <summary>Reproducible, non-persisted handoff to the later Phase 5F persistence packet.</summary>
public sealed record PositionSizingEvaluationBundle(
    PersistedEntryStrategyEvaluation SourceEvaluation,
    PositionSizingInput Input,
    PositionSizingResult Result);

/// <summary>Assembles one reproducible Position Sizing evaluation from the Phase 4 source and current snapshots.</summary>
public interface IPositionSizingEvaluationOrchestrator
{
    Task<PositionSizingEvaluationBundle> EvaluateAsync(
        Guid entryStrategyEvaluationId,
        DateTimeOffset sizingTimestampUtc,
        CancellationToken cancellationToken = default);
}

/// <summary>Append-only persistence boundary for complete Position Sizing evaluations.</summary>
public interface IPositionSizingEvaluationRepository
{
    Task InsertAsync(PositionSizingEvaluation evaluation, CancellationToken cancellationToken = default);
    Task<PositionSizingEvaluation?> GetByIdAsync(Guid positionSizingEvaluationId,
        CancellationToken cancellationToken = default);
}

/// <summary>Application write boundary for immutable Position Sizing evaluations.</summary>
public interface IPositionSizingEvaluationWriter
{
    Task<PositionSizingEvaluation> CreateAsync(Guid entryStrategyEvaluationId, DateTimeOffset sizingTimestampUtc,
        CancellationToken cancellationToken = default);
}

public sealed class EntryStrategyEvaluationNotFoundException(Guid evaluationId)
    : InvalidOperationException($"Entry strategy evaluation '{evaluationId}' was not found.");

public sealed class PositionSizingSourceInconsistencyException(string message)
    : InvalidOperationException(message);
