using CMPlus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CMPlus.Infrastructure.Persistence.Configurations;

/// <summary>EF configuration for <see cref="CpmRunRelation"/> (ADR-0019, domain-rules.md
/// weather-eot §4.3 - "not optional": the run's network topology, not just per-activity floats).
/// <c>PredecessorActivityId</c>/<c>SuccessorActivityId</c> are not EF-level FKs, same convention as
/// <see cref="CpmRunActivityConfiguration"/>'s <c>ActivityId</c>.</summary>
public sealed class CpmRunRelationConfiguration : IEntityTypeConfiguration<CpmRunRelation>
{
    public void Configure(EntityTypeBuilder<CpmRunRelation> builder)
    {
        builder.ToTable("CpmRunRelations");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.TenantId).IsRequired();

        // Mirrors CpmRunActivityConfiguration's identical index - "give me this run's relations" is
        // satisfied via CpmRun.Relations (ICpmRunHistoryReader.GetGoverningRunAsync's Include).
        builder.HasIndex(r => new { r.TenantId, r.CpmRunId });

        // Append-only (ADR-0019): no update/delete path exists on the entity (no mutator methods/
        // setters) or here in configuration. Structurally enforced by AppendOnlyGuardInterceptor via
        // the IAppendOnly marker (security review sprint-09.md M-01 pattern).
    }
}
