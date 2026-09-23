namespace Aegis.Infrastructure.Persistence.Repositories;

using Aegis.Modules.Audit.Application;
using Aegis.Modules.Audit.Domain;
using Aegis.Modules.Identity.Application;
using Aegis.Modules.Identity.Domain;
using Aegis.Shared.Domain;
using Microsoft.EntityFrameworkCore;

public sealed class TenantRepository : ITenantRepository
{
    private readonly AegisDbContext _db;

    public TenantRepository(AegisDbContext db) => _db = db;

    public Task<Tenant?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _db.Tenants.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public Task<Tenant?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        var normalized = slug.Trim().ToLowerInvariant();
        return _db.Tenants.FirstOrDefaultAsync(t => t.Slug == normalized, cancellationToken);
    }

    public async Task AddAsync(Tenant tenant, CancellationToken cancellationToken = default)
    {
        await _db.Tenants.AddAsync(tenant, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
    }
}

public sealed class UserRepository : IUserRepository
{
    private readonly AegisDbContext _db;

    public UserRepository(AegisDbContext db) => _db = db;

    public Task<User?> GetByTenantAndIdAsync(TenantId tenantId, Guid id, CancellationToken cancellationToken = default)
        => _db.Users.FirstOrDefaultAsync(u => u.TenantId == tenantId && u.Id == id, cancellationToken);

    public Task<User?> GetByTenantAndEmailAsync(TenantId tenantId, string email, CancellationToken cancellationToken = default)
    {
        var normalized = email.Trim().ToLowerInvariant();
        return _db.Users.FirstOrDefaultAsync(u => u.TenantId == tenantId && u.Email == normalized, cancellationToken);
    }

    public async Task AddAsync(User user, CancellationToken cancellationToken = default)
    {
        await _db.Users.AddAsync(user, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(User user, CancellationToken cancellationToken = default)
    {
        _db.Users.Update(user);
        await _db.SaveChangesAsync(cancellationToken);
    }
}

public sealed class AuditWriter : IAuditWriter
{
    private readonly AegisDbContext _db;

    public AuditWriter(AegisDbContext db) => _db = db;

    public async Task AppendAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        await _db.AuditEvents.AddAsync(auditEvent, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
    }
}

public sealed class AuditEventRepository : IAuditEventRepository
{
    private readonly AegisDbContext _db;

    public AuditEventRepository(AegisDbContext db) => _db = db;

    public Task<AuditEvent?> GetByTenantAndIdAsync(Guid tenantId, Guid id, CancellationToken cancellationToken = default)
        => _db.AuditEvents.AsNoTracking()
            .FirstOrDefaultAsync(e => e.TenantId == tenantId && e.Id == id, cancellationToken);
}
