namespace Aegis.Infrastructure.Persistence.Configurations.Cases;

using System.Text.Json;
using Aegis.Modules.Cases.Domain;
using Aegis.Shared.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public sealed class ComplianceCaseConfiguration : IEntityTypeConfiguration<ComplianceCase>
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public void Configure(EntityTypeBuilder<ComplianceCase> builder)
    {
        builder.ToTable("cases", "cases");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TenantId)
            .HasConversion(id => id.Value, value => new TenantId(value))
            .HasColumnName("tenant_id")
            .IsRequired();
        builder.Property(x => x.CustomerId).HasColumnName("customer_id");
        builder.Property(x => x.Title).HasMaxLength(300).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.Priority).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.AssignedTo).HasMaxLength(200);
        builder.Property(x => x.OpenedAt).IsRequired();
        builder.Property(x => x.ClosedAt);
        builder.Property(x => x.Disposition).HasConversion<string>().HasMaxLength(64);
        builder.Property(x => x.Conclusion).HasMaxLength(4000);

        builder.Property(x => x.LinkedAlertIds)
            .HasColumnName("linked_alert_ids_json")
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v ?? new List<Guid>(), JsonOptions),
                v => JsonSerializer.Deserialize<List<Guid>>(v, JsonOptions) ?? new List<Guid>())
            .Metadata.SetValueComparer(new ValueComparer<List<Guid>>(
                (a, b) => JsonSerializer.Serialize(a, JsonOptions) == JsonSerializer.Serialize(b, JsonOptions),
                v => JsonSerializer.Serialize(v, JsonOptions).GetHashCode(),
                v => JsonSerializer.Deserialize<List<Guid>>(JsonSerializer.Serialize(v, JsonOptions), JsonOptions)!));

        builder.Property(x => x.Notes)
            .HasColumnName("notes_json")
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v ?? new List<CaseNote>(), JsonOptions),
                v => JsonSerializer.Deserialize<List<CaseNote>>(v, JsonOptions) ?? new List<CaseNote>())
            .Metadata.SetValueComparer(new ValueComparer<List<CaseNote>>(
                (a, b) => JsonSerializer.Serialize(a, JsonOptions) == JsonSerializer.Serialize(b, JsonOptions),
                v => JsonSerializer.Serialize(v, JsonOptions).GetHashCode(),
                v => JsonSerializer.Deserialize<List<CaseNote>>(JsonSerializer.Serialize(v, JsonOptions), JsonOptions)!));

        builder.HasIndex(x => new { x.TenantId, x.Status });
        builder.Ignore(x => x.DomainEvents);
    }
}
