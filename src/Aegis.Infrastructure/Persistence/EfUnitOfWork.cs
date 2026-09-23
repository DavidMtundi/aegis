namespace Aegis.Infrastructure.Persistence;

using Aegis.Shared.Persistence;

public sealed class EfUnitOfWork : IUnitOfWork
{
    private readonly AegisDbContext _db;

    public EfUnitOfWork(AegisDbContext db) => _db = db;

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => _db.SaveChangesAsync(cancellationToken);
}
