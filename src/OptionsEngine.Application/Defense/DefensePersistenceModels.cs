using OptionsEngine.Strategy.Defense;

namespace OptionsEngine.Application.Defense;

/// <summary>Identity-preserving Phase 6 current-position read boundary; no transaction or campaign semantics.</summary>
public interface ICurrentOpenShortCallPositionRepository
{
    Task<OpenShortCallPositionSnapshot?> GetByIdAsync(long openShortCallPositionId,
        CancellationToken cancellationToken = default);
}
