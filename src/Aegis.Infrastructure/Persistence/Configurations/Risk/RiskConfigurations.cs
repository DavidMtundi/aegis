namespace Aegis.Infrastructure.Persistence.Configurations.Risk;

using System.Text.Json;
using System.Text.Json.Serialization;
using Aegis.Modules.Risk.Domain;
using Aegis.Shared.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

internal static class RiskJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T Read<T>(string json) where T : new() => JsonSerializer.Deserialize<T>(json, Options) ?? new T();

    public static ValueComparer<T> Comparer<T>() where T : new() => new(
        (a, b) => Write(a) == Write(b),
        v => Write(v).GetHashCode(),
        v => Read<T>(Write(v)));
}

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
            .HasConversion(v => RiskJson.Write(v), v => RiskJson.Read<List<RiskFactorDefinition>>(v))
            .Metadata.SetValueComparer(RiskJson.Comparer<List<RiskFactorDefinition>>());

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
            .HasConversion(v => RiskJson.Write(v), v => RiskJson.Read<List<RiskContribution>>(v))
            .Metadata.SetValueComparer(RiskJson.Comparer<List<RiskContribution>>());

        builder.HasIndex(x => new { x.TenantId, x.CustomerId, x.CalculatedAt });
    }
}
