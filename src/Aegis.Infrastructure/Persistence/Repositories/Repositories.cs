namespace Aegis.Infrastructure.Persistence.Repositories;

using Aegis.Modules.Aml.Application;
using Aegis.Modules.Aml.Domain;
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
    }

    public async Task UpdateAsync(User user, CancellationToken cancellationToken = default)
    {
        _db.Users.Update(user);
    }
}

public sealed class AuditWriter : IAuditWriter
{
    private readonly AegisDbContext _db;

    public AuditWriter(AegisDbContext db) => _db = db;

    public async Task AppendAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        await _db.AuditEvents.AddAsync(auditEvent, cancellationToken);
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

public sealed class CustomerRepository : Aegis.Modules.Customers.Application.ICustomerRepository
{
    private readonly AegisDbContext _db;
    public CustomerRepository(AegisDbContext db) => _db = db;

    public Task<Aegis.Modules.Customers.Domain.Customer?> GetByTenantAndIdAsync(TenantId tenantId, Guid id, CancellationToken cancellationToken = default)
        => _db.Customers.FirstOrDefaultAsync(c => c.TenantId == tenantId && c.Id == id, cancellationToken);

    public async Task AddAsync(Aegis.Modules.Customers.Domain.Customer customer, CancellationToken cancellationToken = default)
    {
        await _db.Customers.AddAsync(customer, cancellationToken);
    }
}

public sealed class AccountRepository : Aegis.Modules.Customers.Application.IAccountRepository
{
    private readonly AegisDbContext _db;
    public AccountRepository(AegisDbContext db) => _db = db;

    public Task<Aegis.Modules.Customers.Domain.Account?> GetByTenantAndIdAsync(TenantId tenantId, Guid id, CancellationToken cancellationToken = default)
        => _db.Accounts.FirstOrDefaultAsync(a => a.TenantId == tenantId && a.Id == id, cancellationToken);

    public async Task AddAsync(Aegis.Modules.Customers.Domain.Account account, CancellationToken cancellationToken = default)
    {
        await _db.Accounts.AddAsync(account, cancellationToken);
    }
}

public sealed class TransactionRepository :
    Aegis.Modules.Transactions.Application.ITransactionRepository,
    Aegis.Modules.Transactions.Application.ITransactionReadPort
{
    private readonly AegisDbContext _db;
    public TransactionRepository(AegisDbContext db) => _db = db;

    public Task<Aegis.Modules.Transactions.Domain.CanonicalTransaction?> GetByTenantAndIdAsync(TenantId tenantId, Guid id, CancellationToken cancellationToken = default)
        => _db.Transactions.FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Id == id, cancellationToken);

    public Task<Aegis.Modules.Transactions.Domain.CanonicalTransaction?> GetByTenantAndExternalReferenceAsync(TenantId tenantId, string externalReference, CancellationToken cancellationToken = default)
    {
        var key = externalReference.Trim();
        return _db.Transactions.FirstOrDefaultAsync(t => t.TenantId == tenantId && t.ExternalReference == key, cancellationToken);
    }

    public async Task AddAsync(Aegis.Modules.Transactions.Domain.CanonicalTransaction transaction, CancellationToken cancellationToken = default)
    {
        await _db.Transactions.AddAsync(transaction, cancellationToken);
    }

    public async Task<IReadOnlyList<Aegis.Modules.Transactions.Domain.CanonicalTransaction>> GetForCustomerWindowAsync(
        TenantId tenantId,
        CustomerId customerId,
        DateTimeOffset windowStartInclusive,
        DateTimeOffset windowEndExclusive,
        CancellationToken cancellationToken = default)
    {
        // Include Local (Added) entities so feature calc sees the in-flight ingest before SaveChanges.
        var fromDb = await _db.Transactions
            .Where(t => t.TenantId == tenantId
                        && t.CustomerId == customerId
                        && t.Timestamp >= windowStartInclusive
                        && t.Timestamp < windowEndExclusive)
            .OrderBy(t => t.Timestamp)
            .ToListAsync(cancellationToken);

        var fromLocal = _db.Transactions.Local
            .Where(t => t.TenantId == tenantId
                        && t.CustomerId == customerId
                        && t.Timestamp >= windowStartInclusive
                        && t.Timestamp < windowEndExclusive)
            .ToList();

        return fromDb
            .Concat(fromLocal)
            .GroupBy(t => t.Id)
            .Select(g => g.First())
            .OrderBy(t => t.Timestamp)
            .ToList();
    }
}

public sealed class AmlRuleRepository : IAmlRuleRepository
{
    private readonly AegisDbContext _db;
    public AmlRuleRepository(AegisDbContext db) => _db = db;

    public Task<AmlRule?> GetByTenantAndCodeAsync(TenantId tenantId, string code, CancellationToken cancellationToken = default)
        => _db.AmlRules.FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Code == code, cancellationToken);

    public async Task AddAsync(AmlRule rule, CancellationToken cancellationToken = default)
        => await _db.AmlRules.AddAsync(rule, cancellationToken);
}

public sealed class AmlRuleVersionRepository : IAmlRuleVersionRepository
{
    private readonly AegisDbContext _db;
    public AmlRuleVersionRepository(AegisDbContext db) => _db = db;

    public async Task<IReadOnlyList<AmlRuleVersion>> GetActiveByTenantAsync(TenantId tenantId, CancellationToken cancellationToken = default)
        => await _db.AmlRuleVersions
            .Where(v => v.TenantId == tenantId && v.Status == RuleVersionStatus.ACTIVE)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(AmlRuleVersion version, CancellationToken cancellationToken = default)
        => await _db.AmlRuleVersions.AddAsync(version, cancellationToken);
}
