using CMPlus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CMPlus.Infrastructure.Persistence.Configurations;

/// <summary>EF configuration for <see cref="ManpowerPlan"/> - domain-rules.md (manpower-equipment)
/// §4.6(a). Ordinary mutable aggregate (unlike its sibling <see cref="ManpowerEquipmentLog"/>) - no
/// <see cref="CMPlus.Domain.Common.IAppendOnly"/> here, and no update/delete restriction: a plan is
/// revised in place via <see cref="ManpowerPlan.Revise"/>, and every revision is still audited by the
/// default <c>AuditSaveChangesInterceptor</c> behaviour.</summary>
public sealed class ManpowerPlanConfiguration : IEntityTypeConfiguration<ManpowerPlan>
{
    public void Configure(EntityTypeBuilder<ManpowerPlan> builder)
    {
        builder.ToTable("ManpowerPlans");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.TenantId).IsRequired();
        builder.Property(p => p.PlannedManHours).HasPrecision(9, 2);

        // The read GetManningInputsAsync/GetPlannedManHours(d) both need: "which plan row(s) cover
        // this scope on this date" - TenantId-leading, ProjectId then WbsNodeId then the date range.
        builder.HasIndex(p => new { p.TenantId, p.ProjectId, p.WbsNodeId, p.EffectiveFrom, p.EffectiveTo });

        builder.ToTable(tb =>
        {
            tb.HasCheckConstraint("CK_ManpowerPlans_PlannedWorkerCount", "[PlannedWorkerCount] IS NULL OR [PlannedWorkerCount] >= 0");
            tb.HasCheckConstraint("CK_ManpowerPlans_PlannedManHours", "[PlannedManHours] IS NULL OR [PlannedManHours] >= 0");
            tb.HasCheckConstraint("CK_ManpowerPlans_EffectiveRange", "[EffectiveTo] >= [EffectiveFrom]");
        });
    }
}
