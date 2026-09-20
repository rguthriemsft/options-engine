using OptionsEngine.Strategy.Defense;

namespace OptionsEngine.Application.Defense;

/// <summary>Identity-preserving Phase 6 current-position read boundary; no transaction or campaign semantics.</summary>
public interface ICurrentOpenShortCallPositionRepository
{
    Task<OpenShortCallPositionSnapshot?> GetByIdAsync(long openShortCallPositionId,
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
