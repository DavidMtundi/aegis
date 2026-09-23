namespace Aegis.Infrastructure.Persistence.Configurations.Transactions;

using Aegis.Modules.Transactions.Domain;
using Aegis.Shared.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public sealed class CanonicalTransactionConfiguration : IEntityTypeConfiguration<CanonicalTransaction>
{
    public void Configure(EntityTypeBuilder<CanonicalTransaction> builder)
    {
        builder.ToTable("transactions", "transactions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TenantId)
            .HasConversion(id => id.Value, value => new TenantId(value))
            .HasColumnName("tenant_id")
            .IsRequired();
        builder.Property(x => x.TransactionId)
            .HasConversion(id => id.Value, value => new TransactionId(value))
            .HasColumnName("transaction_id")
            .IsRequired();
        builder.Property(x => x.ExternalReference).HasMaxLength(200).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.ExternalReference }).IsUnique();
        builder.Property(x => x.AccountId)
            .HasConversion(id => id.Value, value => new AccountId(value))
            .HasColumnName("account_id")
            .IsRequired();
        builder.Property(x => x.CustomerId)
            .HasConversion(id => id.Value, value => new CustomerId(value))
            .HasColumnName("customer_id")
            .IsRequired();
        builder.OwnsOne(x => x.Amount, money =>
        {
            money.Property(m => m.Amount).HasColumnName("amount").HasColumnType("numeric(18,2)");
            money.Property(m => m.Currency).HasColumnName("currency").HasMaxLength(3);
        });
        builder.Property(x => x.Direction).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.TransactionType).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.Channel).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.SourceAccountId)
            .HasConversion(id => id.HasValue ? id.Value.Value : (Guid?)null, value => value.HasValue ? new AccountId(value.Value) : null);
        builder.Property(x => x.DestinationAccountId)
            .HasConversion(id => id.HasValue ? id.Value.Value : (Guid?)null, value => value.HasValue ? new AccountId(value.Value) : null);
        builder.Property(x => x.SourceCustomerId)
            .HasConversion(id => id.HasValue ? id.Value.Value : (Guid?)null, value => value.HasValue ? new CustomerId(value.Value) : null);
        builder.Property(x => x.DestinationCustomerId)
            .HasConversion(id => id.HasValue ? id.Value.Value : (Guid?)null, value => value.HasValue ? new CustomerId(value.Value) : null);
        builder.Property(x => x.SourceCountry).HasMaxLength(2);
        builder.Property(x => x.DestinationCountry).HasMaxLength(2);
        builder.Property(x => x.CounterpartyCountry).HasMaxLength(2);
        builder.Property(x => x.ProductCode).HasMaxLength(50);
        builder.Property(x => x.CounterpartyId).HasMaxLength(100);
        builder.Property(x => x.RawSourceId).HasMaxLength(100);
        builder.Property(x => x.Metadata)
            .HasColumnName("metadata")
            .HasColumnType("jsonb")
            .HasConversion(
                v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                v => string.IsNullOrWhiteSpace(v)
                    ? new Dictionary<string, string>()
                    : System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(v, (System.Text.Json.JsonSerializerOptions?)null)
                      ?? new Dictionary<string, string>());
        builder.Ignore(x => x.DomainEvents);
        builder.HasIndex(x => new { x.TenantId, x.CustomerId, x.Timestamp });
    }
}
