namespace Aegis.Infrastructure.Persistence;

using Aegis.Modules.Aml.Domain;
using Aegis.Modules.Alerts.Domain;
using Aegis.Modules.Audit.Domain;
using Aegis.Modules.Cases.Domain;
using Aegis.Modules.Customers.Domain;
using Aegis.Modules.Identity.Domain;
using Aegis.Modules.Transactions.Domain;
using Aegis.Shared.Domain;
using Aegis.Shared.Security;
using Microsoft.EntityFrameworkCore;

public sealed class AegisDbContext : DbContext
{
    private readonly ITenantContext? _tenant;

    public AegisDbContext(DbContextOptions<AegisDbContext> options, ITenantContext? tenant = null) : base(options)
    {
        _tenant = tenant;
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<CanonicalTransaction> Transactions => Set<CanonicalTransaction>();
    public DbSet<AmlRule> AmlRules => Set<AmlRule>();
    public DbSet<AmlRuleVersion> AmlRuleVersions => Set<AmlRuleVersion>();
    public DbSet<Alert> Alerts => Set<Alert>();
    public DbSet<ComplianceCase> Cases => Set<ComplianceCase>();

    // Anonymous paths (login, tenant bootstrap) have no tenant yet and stay unfiltered;
    // they must keep explicit tenant predicates in their repositories.
    private bool TenantFilterEnabled => _tenant?.IsAuthenticated == true;
    private TenantId CurrentTenantId => _tenant?.TenantId ?? TenantId.Empty;
    private Guid CurrentTenantGuid => CurrentTenantId.Value;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AegisDbContext).Assembly);

        modelBuilder.Entity<User>().HasQueryFilter(e => !TenantFilterEnabled || e.TenantId == CurrentTenantId);
        modelBuilder.Entity<Role>().HasQueryFilter(e => !TenantFilterEnabled || e.TenantId == CurrentTenantId);
        modelBuilder.Entity<Customer>().HasQueryFilter(e => !TenantFilterEnabled || e.TenantId == CurrentTenantId);
        modelBuilder.Entity<Account>().HasQueryFilter(e => !TenantFilterEnabled || e.TenantId == CurrentTenantId);
        modelBuilder.Entity<CanonicalTransaction>().HasQueryFilter(e => !TenantFilterEnabled || e.TenantId == CurrentTenantId);
        modelBuilder.Entity<AmlRule>().HasQueryFilter(e => !TenantFilterEnabled || e.TenantId == CurrentTenantId);
        modelBuilder.Entity<AmlRuleVersion>().HasQueryFilter(e => !TenantFilterEnabled || e.TenantId == CurrentTenantId);
        modelBuilder.Entity<Alert>().HasQueryFilter(e => !TenantFilterEnabled || e.TenantId == CurrentTenantId);
        modelBuilder.Entity<ComplianceCase>().HasQueryFilter(e => !TenantFilterEnabled || e.TenantId == CurrentTenantId);
        modelBuilder.Entity<AuditEvent>().HasQueryFilter(e => !TenantFilterEnabled || e.TenantId == CurrentTenantGuid);

        base.OnModelCreating(modelBuilder);
    }
}
