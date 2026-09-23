namespace Aegis.Infrastructure.Persistence.Configurations.Audit;

using Aegis.Modules.Audit.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        builder.ToTable("audit_events", "audit");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.EventType).HasMaxLength(100).IsRequired();
        builder.Property(x => x.EntityType).HasMaxLength(100).IsRequired();
        builder.Property(x => x.EntityId).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ActorId).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ActorRole).HasMaxLength(100);
        builder.Property(x => x.BeforeState).HasColumnType("jsonb");
        builder.Property(x => x.AfterState).HasColumnType("jsonb");
        builder.Property(x => x.Reason).HasMaxLength(1000);
        builder.Property(x => x.CorrelationId).HasMaxLength(100);
        builder.HasIndex(x => new { x.TenantId, x.OccurredAt });
        builder.HasIndex(x => new { x.TenantId, x.Id });
    }
}
