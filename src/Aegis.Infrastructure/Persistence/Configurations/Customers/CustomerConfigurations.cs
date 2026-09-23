namespace Aegis.Infrastructure.Persistence.Configurations.Customers;

using Aegis.Modules.Customers.Domain;
using Aegis.Shared.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("customers", "customers");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TenantId)
            .HasConversion(id => id.Value, value => new TenantId(value))
            .HasColumnName("tenant_id")
            .IsRequired();
        builder.Property(x => x.CustomerId)
            .HasConversion(id => id.Value, value => new CustomerId(value))
            .HasColumnName("customer_id")
            .IsRequired();
        builder.Property(x => x.Type).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.ExternalReference).HasMaxLength(100);
        builder.Property(x => x.Country).HasMaxLength(2).IsRequired();
        builder.Property(x => x.FirstName).HasMaxLength(100);
        builder.Property(x => x.LastName).HasMaxLength(100);
        builder.Property(x => x.LegalName).HasMaxLength(200);
        builder.HasIndex(x => new { x.TenantId, x.Id });
        builder.Ignore(x => x.DomainEvents);
    }
}

public sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable("accounts", "customers");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TenantId)
            .HasConversion(id => id.Value, value => new TenantId(value))
            .HasColumnName("tenant_id")
            .IsRequired();
        builder.Property(x => x.AccountId)
            .HasConversion(id => id.Value, value => new AccountId(value))
            .HasColumnName("account_id")
            .IsRequired();
        builder.Property(x => x.CustomerId)
            .HasConversion(id => id.Value, value => new CustomerId(value))
            .HasColumnName("customer_id")
            .IsRequired();
        builder.Property(x => x.ExternalReference).HasMaxLength(100);
        builder.Property(x => x.AccountType).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        builder.HasIndex(x => new { x.TenantId, x.Id });
        builder.HasIndex(x => new { x.TenantId, x.CustomerId });
        builder.Ignore(x => x.DomainEvents);
    }
}
