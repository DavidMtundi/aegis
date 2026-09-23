namespace Aegis.Infrastructure.Persistence;

using Aegis.Modules.Audit.Domain;
using Aegis.Modules.Customers.Domain;
using Aegis.Modules.Identity.Domain;
using Aegis.Modules.Transactions.Domain;
using Microsoft.EntityFrameworkCore;

public sealed class AegisDbContext : DbContext
{
    public AegisDbContext(DbContextOptions<AegisDbContext> options) : base(options)
    {
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<CanonicalTransaction> Transactions => Set<CanonicalTransaction>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AegisDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
