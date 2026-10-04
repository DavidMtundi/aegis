namespace Aegis.Infrastructure.Persistence.Configurations.Screening;

using Aegis.Modules.Screening.Domain;
using Aegis.Shared.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

internal static class ScreeningMapping
{
    public static void Lists<T>(EntityTypeBuilder<T> builder) where T : class
    {
        builder.Property<List<string>>("Names").HasColumnName("names_json").HasColumnType("jsonb")
            .HasConversion(v => JsonColumn.Write(v), v => JsonColumn.Read<List<string>>(v))
            .Metadata.SetValueComparer(JsonColumn.Comparer<List<string>>());
        builder.Property<List<BirthYearRange>>("BirthYears").HasColumnName("birth_years_json").HasColumnType("jsonb")
            .HasConversion(v => JsonColumn.Write(v), v => JsonColumn.Read<List<BirthYearRange>>(v))
            .Metadata.SetValueComparer(JsonColumn.Comparer<List<BirthYearRange>>());
        builder.Property<List<string>>("Countries").HasColumnName("countries_json").HasColumnType("jsonb")
            .HasConversion(v => JsonColumn.Write(v), v => JsonColumn.Read<List<string>>(v))
            .Metadata.SetValueComparer(JsonColumn.Comparer<List<string>>());
    }
}

public sealed class WatchlistEntryConfiguration : IEntityTypeConfiguration<WatchlistEntry>
{
    public void Configure(EntityTypeBuilder<WatchlistEntry> builder)
    {
        builder.ToTable("watchlist_entries", "screening");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Source).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.ExternalId).HasMaxLength(ScreeningLimits.ExternalIdMaxLength).IsRequired();
        builder.Property(x => x.Category).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.Detail).HasMaxLength(ScreeningLimits.DetailMaxLength);
        builder.Property(x => x.LoadId).HasColumnName("load_id");
        ScreeningMapping.Lists(builder);
        builder.HasIndex(x => new { x.Source, x.ExternalId }).IsUnique();
    }
}

public sealed class WatchlistLoadConfiguration : IEntityTypeConfiguration<WatchlistLoad>
{
    public void Configure(EntityTypeBuilder<WatchlistLoad> builder)
    {
        builder.ToTable("watchlist_loads", "screening");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Source).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.ContentHash).HasMaxLength(64).IsRequired();
        builder.HasIndex(x => new { x.Source, x.LoadedAt });
    }
}

public sealed class TenantWatchlistEntryConfiguration : IEntityTypeConfiguration<TenantWatchlistEntry>
{
    public void Configure(EntityTypeBuilder<TenantWatchlistEntry> builder)
    {
        builder.ToTable("tenant_watchlist_entries", "screening");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TenantId)
            .HasConversion(id => id.Value, value => new TenantId(value))
            .HasColumnName("tenant_id")
            .IsRequired();
        builder.Property(x => x.ExternalId).HasMaxLength(ScreeningLimits.ExternalIdMaxLength).IsRequired();
        builder.Property(x => x.Category).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.Detail).HasMaxLength(ScreeningLimits.DetailMaxLength);
        builder.Property(x => x.UploadedBy).HasMaxLength(200).IsRequired();
        ScreeningMapping.Lists(builder);
        builder.HasIndex(x => new { x.TenantId, x.ExternalId }).IsUnique();
    }
}

public sealed class ScreeningMatchConfiguration : IEntityTypeConfiguration<ScreeningMatch>
{
    public void Configure(EntityTypeBuilder<ScreeningMatch> builder)
    {
        builder.ToTable("screening_matches", "screening");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TenantId)
            .HasConversion(id => id.Value, value => new TenantId(value))
            .HasColumnName("tenant_id")
            .IsRequired();
        builder.Property(x => x.CustomerId).HasColumnName("customer_id").IsRequired();
        builder.Property(x => x.CustomerName).HasMaxLength(300).IsRequired();
        builder.Property(x => x.Source).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.ExternalId).HasMaxLength(ScreeningLimits.ExternalIdMaxLength).IsRequired();
        builder.Property(x => x.Category).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.MatchedName).HasMaxLength(ScreeningLimits.NameMaxLength).IsRequired();
        builder.Property(x => x.Trigger).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.ReviewedBy).HasMaxLength(200);
        builder.Property(x => x.ReviewReason).HasMaxLength(ScreeningLimits.ReviewReasonMaxLength);
        builder.Property(x => x.AlertId).HasColumnName("alert_id");
        builder.Property(x => x.Subject).HasColumnName("subject_json").HasColumnType("jsonb")
            .HasConversion(v => JsonColumn.Write(v), v => JsonColumn.Read<WatchlistSubjectJson>(v).ToSubject())
            .Metadata.SetValueComparer(new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<WatchlistSubject>(
                (a, b) => JsonColumn.Write(a) == JsonColumn.Write(b),
                v => JsonColumn.Write(v).GetHashCode(),
                v => v));
        builder.HasIndex(x => new { x.TenantId, x.CustomerId, x.Source, x.ExternalId }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.Status, x.CreatedAt });
        builder.Ignore(x => x.DomainEvents);
    }

    /// <summary>
    /// <see cref="JsonColumn.Read{T}"/> needs a parameterless type; this mirrors <see cref="WatchlistSubject"/>.
    /// </summary>
    private sealed class WatchlistSubjectJson
    {
        public WatchlistSource Source { get; set; }
        public string ExternalId { get; set; } = "";
        public WatchlistCategory Category { get; set; }
        public SubjectKind Kind { get; set; }
        public List<string> Names { get; set; } = new();
        public List<BirthYearRange> BirthYears { get; set; } = new();
        public List<string> Countries { get; set; } = new();
        public string? Detail { get; set; }

        public WatchlistSubject ToSubject() => new(Source, ExternalId, Category, Kind, Names, BirthYears, Countries, Detail);
    }
}
