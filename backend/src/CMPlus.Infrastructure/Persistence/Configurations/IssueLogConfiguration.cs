using CMPlus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CMPlus.Infrastructure.Persistence.Configurations;

/// <summary>EF configuration for <see cref="IssueLog"/> (S11-BE-03/S11-DB-01), shaped by
/// <c>docs/specs/weather-eot/domain-rules.md</c> §9. "ProjectId is not an EF-level FK" mirrors this
/// codebase's established convention (see <see cref="DailyWeatherLogConfiguration"/>'s identical
/// remark).</summary>
public sealed class IssueLogConfiguration : IEntityTypeConfiguration<IssueLog>
{
    public void Configure(EntityTypeBuilder<IssueLog> builder)
    {
        builder.ToTable("IssueLogs");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();

        builder.Property(i => i.TenantId).IsRequired();
        builder.Property(i => i.Title).IsRequired().HasMaxLength(200);
        builder.Property(i => i.Detail).HasMaxLength(2000);
        builder.Property(i => i.Owner).HasMaxLength(200);

        // domain-rules.md §9.2: "two users advancing simultaneously means the second gets 409,
        // never a double-advance - the same discipline as the approval handlers".
        builder.Property(i => i.RowVersion).IsRowVersion();

        // S11-DB-01-shaped baseline: the list/tile-count read (ListIssuesQueryHandler) filters
        // (TenantId, ProjectId) - both the table rows and the tile counts are derived from the SAME
        // query over this same index prefix, so the two can never structurally disagree.
        builder.HasIndex(i => new { i.TenantId, i.ProjectId, i.CreatedAt });

        // Baseline status index - VariationOrderConfiguration's own (TenantId, ProjectId, Status)
        // precedent - defense-in-depth for a future status-filtered list.
        builder.HasIndex(i => new { i.TenantId, i.ProjectId, i.Status });

        // domain-rules.md §9.2's three invariants, DB-layer defense-in-depth mirroring the Domain
        // invariant enforced by IssueLog.AdvanceStatus (docs/db-conventions.md §3.1).
        builder.ToTable(tb =>
        {
            tb.HasCheckConstraint(
                "CK_IssueLogs_ClosedAt_Matches_Status",
                "([Status] = 3 AND [ClosedAt] IS NOT NULL) OR ([Status] <> 3 AND [ClosedAt] IS NULL)");
            tb.HasCheckConstraint(
                "CK_IssueLogs_StartedAt_Matches_Status",
                "([Status] IN (2, 3) AND [StartedAt] IS NOT NULL) OR ([Status] = 1 AND [StartedAt] IS NULL)");
            tb.HasCheckConstraint(
                "CK_IssueLogs_StartedAt_Before_ClosedAt",
                "[StartedAt] IS NULL OR [ClosedAt] IS NULL OR [StartedAt] <= [ClosedAt]");
        });
    }
}
