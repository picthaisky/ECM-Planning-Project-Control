using CMPlus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CMPlus.Infrastructure.Persistence.Configurations;

/// <summary>EF configuration for <see cref="EotEvaluationRun"/> (S11-BE-02, domain-rules.md
/// weather-eot §5.1). <c>CpmRunId</c> is not an EF-level FK - same convention as every other row that
/// cites a <see cref="CpmRun"/> by id without owning its lifecycle.</summary>
public sealed class EotEvaluationRunConfiguration : IEntityTypeConfiguration<EotEvaluationRun>
{
    public void Configure(EntityTypeBuilder<EotEvaluationRun> builder)
    {
        builder.ToTable("EotEvaluationRuns");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.TenantId).IsRequired();

        builder.HasIndex(r => new { r.TenantId, r.EotEvaluationId });
        // EotEvaluationDriver correlates back to this row by CpmRunId (not a generated id) - see
        // EotEvaluationDriverInput's remarks - so this is the lookup a query joining the two needs.
        builder.HasIndex(r => new { r.TenantId, r.CpmRunId });

        builder.ToTable(tb => tb.HasCheckConstraint("CK_EotEvaluationRuns_Window", "[WindowTo] >= [WindowFrom]"));
    }
}
