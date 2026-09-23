namespace Aegis.Infrastructure.Persistence.Configurations.Aml;

using System.Text.Json;
using Aegis.Modules.Aml.Domain;
using Aegis.Shared.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public sealed class AmlRuleConfiguration : IEntityTypeConfiguration<AmlRule>
{
    public void Configure(EntityTypeBuilder<AmlRule> builder)
    {
        builder.ToTable("rules", "aml");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TenantId)
            .HasConversion(id => id.Value, value => new TenantId(value))
            .HasColumnName("tenant_id")
            .IsRequired();
        builder.Property(x => x.Code).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.ScenarioType).HasConversion<string>().HasMaxLength(64);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.CreatedBy).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        builder.Ignore(x => x.Versions);
        builder.Ignore(x => x.DomainEvents);
    }
}

public sealed class AmlRuleVersionConfiguration : IEntityTypeConfiguration<AmlRuleVersion>
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public void Configure(EntityTypeBuilder<AmlRuleVersion> builder)
    {
        builder.ToTable("rule_versions", "aml");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TenantId)
            .HasConversion(id => id.Value, value => new TenantId(value))
            .HasColumnName("tenant_id")
            .IsRequired();
        builder.Property(x => x.RuleId).HasColumnName("rule_id").IsRequired();
        builder.Property(x => x.VersionNumber).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ApprovedBy).HasMaxLength(100);
        builder.Property(x => x.Definition)
            .HasColumnName("definition_json")
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<RuleDefinition>(v, JsonOptions) ?? new RuleDefinition())
            .Metadata.SetValueComparer(new ValueComparer<RuleDefinition>(
                (a, b) => JsonSerializer.Serialize(a, (JsonSerializerOptions?)null) == JsonSerializer.Serialize(b, (JsonSerializerOptions?)null),
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null).GetHashCode(),
                v => JsonSerializer.Deserialize<RuleDefinition>(JsonSerializer.Serialize(v, (JsonSerializerOptions?)null), JsonOptions)!));
        builder.Ignore(x => x.DefinitionJson);
        builder.Ignore(x => x.RuleCode);
        builder.Ignore(x => x.RuleName);
        builder.HasIndex(x => new { x.TenantId, x.RuleId, x.VersionNumber }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.Status });
    }
}
