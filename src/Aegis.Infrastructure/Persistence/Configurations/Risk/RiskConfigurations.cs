namespace Aegis.Infrastructure.Persistence.Configurations.Risk;

using Aegis.Infrastructure.Persistence.Configurations;
using Aegis.Modules.Risk.Domain;
using Aegis.Shared.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public sealed class RiskModelConfiguration : IEntityTypeConfiguration<RiskModel>
{
    public void Configure(EntityTypeBuilder<RiskModel> builder)
    {
        builder.ToTable("risk_models", "risk");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TenantId)
            .HasConversion(id => id.Value, value => new TenantId(value))
            .HasColumnName("tenant_id")
            .IsRequired();
        builder.Property(x => x.Version).IsRequired();
        builder.Property(x => x.IsActive).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(200).IsRequired();

        builder.Property(x => x.Factors)
            .HasColumnName("factors_json")
            .HasColumnType("jsonb")
            .HasConversion(v => JsonColumn.Write(v), v => JsonColumn.Read<List<RiskFactorDefinition>>(v))
            .Metadata.SetValueComparer(JsonColumn.Comparer<List<RiskFactorDefinition>>());

        builder.OwnsOne(x => x.Bands, b =>
        {
            b.Property(p => p.Medium).HasColumnName("band_medium");
            b.Property(p => p.High).HasColumnName("band_high");
            b.Property(p => p.Critical).HasColumnName("band_critical");
        });
        builder.Navigation(x => x.Bands).IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.Version }).IsUnique();
        builder.Ignore(x => x.DomainEvents);
    }
}

public sealed class CustomerRiskScoreConfiguration : IEntityTypeConfiguration<CustomerRiskScore>
{
    public void Configure(EntityTypeBuilder<CustomerRiskScore> builder)
    {
        builder.ToTable("customer_risk_scores", "risk");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TenantId)
            .HasConversion(id => id.Value, value => new TenantId(value))
            .HasColumnName("tenant_id")
            .IsRequired();
        builder.Property(x => x.CustomerId).HasColumnName("customer_id").IsRequired();
        builder.Property(x => x.Band).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.Trigger).HasMaxLength(32).IsRequired();
        builder.Property(x => x.CalculatedBy).HasMaxLength(200).IsRequired();

        builder.Property(x => x.Contributions)
            .HasColumnName("contributions_json")
            .HasColumnType("jsonb")
            .HasConversion(v => JsonColumn.Write(v), v => JsonColumn.Read<List<RiskContribution>>(v))
            .Metadata.SetValueComparer(JsonColumn.Comparer<List<RiskContribution>>());

        builder.HasIndex(x => new { x.TenantId, x.CustomerId, x.CalculatedAt });
    }
}
