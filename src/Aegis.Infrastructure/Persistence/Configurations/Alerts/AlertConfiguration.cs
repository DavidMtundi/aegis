namespace Aegis.Infrastructure.Persistence.Configurations.Alerts;

using System.Text.Json;
using Aegis.Modules.Alerts.Domain;
using Aegis.Shared.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public sealed class AlertConfiguration : IEntityTypeConfiguration<Alert>
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public void Configure(EntityTypeBuilder<Alert> builder)
    {
        builder.ToTable("alerts", "alerts");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TenantId)
            .HasConversion(id => id.Value, value => new TenantId(value))
            .HasColumnName("tenant_id")
            .IsRequired();
        builder.Property(x => x.RuleId).HasColumnName("rule_id").IsRequired();
        builder.Property(x => x.RuleVersionId).HasColumnName("rule_version_id").IsRequired();
        builder.Property(x => x.FocusType).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.FocusEntityId).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Severity).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.RiskScore).IsRequired();
        builder.Property(x => x.TriggeredAt).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.DeduplicationKey).HasColumnName("deduplication_key").HasMaxLength(500).IsRequired();
        builder.Property(x => x.AssignedTo).HasMaxLength(200);
        builder.Property(x => x.Evidence)
            .HasColumnName("evidence_json")
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<AlertEvidence>(v, JsonOptions) ?? new AlertEvidence())
            .Metadata.SetValueComparer(new ValueComparer<AlertEvidence>(
                (a, b) => JsonSerializer.Serialize(a, (JsonSerializerOptions?)null) == JsonSerializer.Serialize(b, (JsonSerializerOptions?)null),
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null).GetHashCode(),
                v => JsonSerializer.Deserialize<AlertEvidence>(JsonSerializer.Serialize(v, (JsonSerializerOptions?)null), JsonOptions)!));
        builder.HasIndex(x => new { x.TenantId, x.DeduplicationKey }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.Status });
        builder.Ignore(x => x.DomainEvents);
    }
}
