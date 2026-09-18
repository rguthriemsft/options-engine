using Microsoft.EntityFrameworkCore;
using OptionsEngine.Application.EntryStrategy;
using OptionsEngine.Domain.Accounts;

namespace OptionsEngine.Infrastructure.Persistence;

/// <summary>Read-only holding lookup used by Application strategy orchestration.</summary>
public sealed class SqliteHoldingRepository(OptionsEngineDbContext db) : IHoldingRepository
{
    public Task<Holding?> GetByIdAsync(Guid holdingId, CancellationToken cancellationToken = default) =>
        db.Holdings.AsNoTracking().SingleOrDefaultAsync(x => x.HoldingId == holdingId, cancellationToken);
}
