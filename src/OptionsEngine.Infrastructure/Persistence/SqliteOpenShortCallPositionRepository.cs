using Microsoft.EntityFrameworkCore;
using OptionsEngine.Application.PositionSizing;
using OptionsEngine.Strategy.PositionSizing;

namespace OptionsEngine.Infrastructure.Persistence;

/// <summary>Reads the current minimal open short-call position snapshot for Position Sizing assembly.</summary>
public sealed class SqliteOpenShortCallPositionRepository(OptionsEngineDbContext db) : IOpenShortCallPositionRepository
{
    public async Task<IReadOnlyList<ExistingShortCallExposure>> GetOpenShortCallsByHoldingAsync(Guid holdingId,
        CancellationToken cancellationToken = default) =>
        await db.OpenShortCallPositions.AsNoTracking()
            .Where(x => x.HoldingId == holdingId)
            .OrderBy(x => x.OptionSymbol).ThenBy(x => x.Expiration).ThenBy(x => x.Strike)
            .ThenBy(x => x.OpenShortCallPositionId)
            .Select(x => new ExistingShortCallExposure(x.HoldingId, x.OptionSymbol, x.Contracts, x.Strike, x.Expiration))
            .ToListAsync(cancellationToken);
}
