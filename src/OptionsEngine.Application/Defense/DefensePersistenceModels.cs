using OptionsEngine.Strategy.Defense;
using OptionsEngine.Strategy.EntryStrategy;
using OptionsEngine.Strategy.Indicators;
using OptionsEngine.Domain.Accounts;

namespace OptionsEngine.Application.Defense;

/// <summary>Identity-preserving Phase 6 current-position read boundary; no transaction or campaign semantics.</summary>
public interface ICurrentOpenShortCallPositionRepository
{
    Task<OpenShortCallPositionSnapshot?> GetByIdAsync(Guid holdingId, long openShortCallPositionId,
        CancellationToken cancellationToken = default);
}

/// <summary>Read-only normalized option observations required for Phase 6 analytical assembly.</summary>
public interface IDefenseMarketDataRepository
{
    Task<DefenseOptionObservation?> GetLatestOptionObservationAsync(string optionSymbol, string provider,
        DateTimeOffset evaluationTimestampUtc, CancellationToken cancellationToken = default);

    Task<DefenseOptionObservation?> GetPreviousTradingDateOptionObservationAsync(string optionSymbol,
        string provider, DateTimeOffset currentObservationTimestampUtc, DateTimeOffset evaluationTimestampUtc,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SelectedRollChainSnapshot>> GetLatestOptionChainsAsync(string underlyingSymbol,
        string provider, DateOnly minimumExpiration, DateOnly maximumExpiration,
        DateTimeOffset evaluationTimestampUtc, CancellationToken cancellationToken = default);
}

/// <summary>Lazy Phase 4 CCOS calculation boundary for a Phase 6 defensive disposition.</summary>
public interface ICurrentCcosResolver
{
    Task<CurrentCcosContext?> ResolveAsync(Holding holding, EarningsContext earnings,
        DateTimeOffset evaluationTimestampUtc, ConfigurationVersion configurationVersion,
        CancellationToken cancellationToken = default);
}

/// <summary>Server-owned, validated Phase 6 configuration and independent algorithm identities.</summary>
public sealed record DefenseRollConfiguration
{
    public required DefenseConfiguration Defense { get; init; }
    public required RollConfiguration Roll { get; init; }
    public required DefenseStrategyVersion DefenseStrategyVersion { get; init; }
    public required RollStrategyVersion RollStrategyVersion { get; init; }

    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Defense); ArgumentNullException.ThrowIfNull(Roll);
        ArgumentNullException.ThrowIfNull(DefenseStrategyVersion); ArgumentNullException.ThrowIfNull(RollStrategyVersion);
        Defense.Validate(); Roll.Validate();
        if (Defense.Version != Roll.Version)
            throw new ArgumentException("Defense and Roll configuration versions must match.");
    }
}

/// <summary>Complete in-memory Phase 6E output; persistence is intentionally deferred to Phase 6F.</summary>
public sealed record DefenseEvaluationBundle(DefenseEvaluation DefenseEvaluation,
    RollEvaluation? RollEvaluation);

public interface IDefenseEvaluationOrchestrator
{
    Task<DefenseEvaluationBundle> EvaluateAsync(Guid holdingId, long openShortCallPositionId,
        DateTimeOffset defenseEvaluationTimestampUtc, CancellationToken cancellationToken = default);
}

public sealed class OpenShortCallPositionNotFoundException(Guid holdingId, long positionId)
    : InvalidOperationException(
        $"Open short-call position '{positionId}' was not found for Holding '{holdingId}'.");

public sealed class InvalidOpenShortCallPositionException(long positionId, string message)
    : InvalidOperationException($"Open short-call position '{positionId}' is invalid: {message}");
