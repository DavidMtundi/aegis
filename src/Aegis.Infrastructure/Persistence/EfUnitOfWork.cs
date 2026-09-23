namespace Aegis.Infrastructure.Persistence;

using Aegis.Shared.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

public sealed class EfUnitOfWork : IUnitOfWork
{
    private readonly AegisDbContext _db;

    public EfUnitOfWork(AegisDbContext db) => _db = db;

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (TryGetUniqueConstraint(ex, out var constraintName))
        {
            // Failed SaveChanges leaves tracked entities; detach so callers can re-query cleanly.
            foreach (var entry in _db.ChangeTracker.Entries().ToList())
            {
                entry.State = EntityState.Detached;
            }

            throw new UniqueConstraintViolationException(constraintName, ex);
        }
    }

    private static bool TryGetUniqueConstraint(DbUpdateException ex, out string? constraintName)
    {
        constraintName = null;
        for (Exception? inner = ex.InnerException; inner is not null; inner = inner.InnerException)
        {
            if (inner is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg)
            {
                constraintName = pg.ConstraintName;
                return true;
            }
        }

        return false;
    }
}
