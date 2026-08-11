using CMPlus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CMPlus.Infrastructure.Persistence.Configurations;

/// <summary>EF configuration for <see cref="EotEvaluationSource"/> (S11-BE-02, domain-rules.md
/// weather-eot §3/§8.5). <c>DailyWeatherLogId</c> is not an EF-level FK - same convention as every
/// other row that cites a <see cref="DailyWeatherLog"/> by id without owning its lifecycle.</summary>
public sealed class EotEvaluationSourceConfiguration : IEntityTypeConfiguration<EotEvaluationSource>
{
    public void Configure(EntityTypeBuilder<EotEvaluationSource> builder)
    {
        builder.ToTable("EotEvaluationSources");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.TenantId).IsRequired();
        builder.Property(s => s.CountableDays).HasPrecision(5, 2);

        builder.HasIndex(s => new { s.TenantId, s.EotEvaluationId });
        builder.HasIndex(s => new { s.TenantId, s.DailyWeatherLogId });

        builder.ToTable(tb => tb.HasCheckConstraint("CK_EotEvaluationSources_CountableDays", "[CountableDays] >= 0"));
    }
}
