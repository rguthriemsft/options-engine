using Microsoft.EntityFrameworkCore;
using OptionsEngine.Application.PositionSizing;
using OptionsEngine.Application.Defense;
using OptionsEngine.Strategy.PositionSizing;
using OptionsEngine.Strategy.Defense;

namespace OptionsEngine.Infrastructure.Persistence;

/// <summary>Reads the current minimal open short-call position snapshot for Position Sizing assembly.</summary>
public sealed class SqliteOpenShortCallPositionRepository(OptionsEngineDbContext db) : IOpenShortCallPositionRepository,
    ICurrentOpenShortCallPositionRepository
{
    public async Task<IReadOnlyList<ExistingShortCallExposure>> GetOpenShortCallsByHoldingAsync(Guid holdingId,
        CancellationToken cancellationToken = default) =>
        await db.OpenShortCallPositions.AsNoTracking()
            .Where(x => x.HoldingId == holdingId)
            .OrderBy(x => x.OptionSymbol).ThenBy(x => x.Expiration).ThenBy(x => x.Strike)
            .ThenBy(x => x.OpenShortCallPositionId)
            .Select(x => new ExistingShortCallExposure(x.HoldingId, x.OptionSymbol, x.Contracts, x.Strike, x.Expiration))
            .ToListAsync(cancellationToken);

    public async Task<OpenShortCallPositionSnapshot?> GetByIdAsync(Guid holdingId, long openShortCallPositionId,
        CancellationToken cancellationToken = default)
    {
        var row = await db.OpenShortCallPositions.AsNoTracking()
            .SingleOrDefaultAsync(x => x.HoldingId == holdingId &&
                x.OpenShortCallPositionId == openShortCallPositionId, cancellationToken);
        return row is null ? null : new OpenShortCallPositionSnapshot(row.OpenShortCallPositionId, row.HoldingId,
            row.OptionSymbol, row.Contracts, row.Strike, row.Expiration, row.OpeningPremiumPerShare, row.OpenedAtUtc);
    }

    public async Task<OpenShortCallPositionSnapshot?> GetByIdAsync(long openShortCallPositionId,
        CancellationToken cancellationToken = default)
    {
        var row = await db.OpenShortCallPositions.AsNoTracking()
            .SingleOrDefaultAsync(x => x.OpenShortCallPositionId == openShortCallPositionId,
                cancellationToken);
        return row is null ? null : new OpenShortCallPositionSnapshot(row.OpenShortCallPositionId,
            row.HoldingId, row.OptionSymbol, row.Contracts, row.Strike, row.Expiration,
            row.OpeningPremiumPerShare, row.OpenedAtUtc);
    }
}
