using Microsoft.EntityFrameworkCore;
using OptionsEngine.Application.EntryStrategy;
using OptionsEngine.Application.PositionSizing;
using OptionsEngine.Domain.Accounts;

namespace OptionsEngine.Infrastructure.Persistence;

/// <summary>Read-only holding lookup used by Application strategy orchestration.</summary>
public sealed class SqliteHoldingRepository(OptionsEngineDbContext db) : IHoldingRepository, IPositionSizingHoldingRepository
{
    public Task<Holding?> GetByIdAsync(Guid holdingId, CancellationToken cancellationToken = default) =>
        db.Holdings.AsNoTracking().SingleOrDefaultAsync(x => x.HoldingId == holdingId, cancellationToken);

    public async Task<IReadOnlyList<Holding>> GetByAccountIdAsync(Guid accountId,
        CancellationToken cancellationToken = default) =>
        await db.Holdings.AsNoTracking().Where(x => x.AccountId == accountId)
            .OrderBy(x => x.HoldingId).ToListAsync(cancellationToken);
}
