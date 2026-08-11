using CMPlus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CMPlus.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF configuration for <see cref="ManpowerEquipmentLog"/> (S12-BE-02, S12-DB), shaped by
/// domain-rules.md (manpower-equipment) §4. <c>ProjectId</c>/<c>WorkCategoryId</c>/<c>WbsNodeId</c>/
/// <c>ActivityId</c> are deliberately not EF-level FK relationships, matching this codebase's
/// existing convention (<see cref="DailyWeatherLog"/>/<see cref="ActualCostEntry"/> do the same) -
/// existence is checked at the Application/repository layer instead.
/// </summary>
public sealed class ManpowerEquipmentLogConfiguration : IEntityTypeConfiguration<ManpowerEquipmentLog>
{
    public void Configure(EntityTypeBuilder<ManpowerEquipmentLog> builder)
    {
        builder.ToTable("ManpowerEquipmentLogs");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();

        builder.Property(m => m.TenantId).IsRequired();
        builder.Property(m => m.SubcontractorRef).HasMaxLength(100);
        builder.Property(m => m.ManHours).HasPrecision(9, 2);
        builder.Property(m => m.OvertimeHours).HasPrecision(9, 2);
        builder.Property(m => m.EquipmentOperatingHours).HasPrecision(9, 2);
        builder.Property(m => m.EquipmentStandbyHours).HasPrecision(9, 2);
        builder.Property(m => m.WorkDescription).HasMaxLength(500);
        builder.Property(m => m.CorrectionReason).HasMaxLength(500);

        // Self-referencing FK for the correction/supersession chain (§4.7), the identical shape
        // DailyWeatherLogConfiguration uses. Restrict, not Cascade: an IAppendOnly row is never
        // legitimately deleted.
        builder.HasOne<ManpowerEquipmentLog>()
            .WithMany()
            .HasForeignKey(m => m.CorrectsLogId)
            .OnDelete(DeleteBehavior.Restrict);

        // §4.5's AMH(a,b] seek - the primary read path for the whole project.
        builder.HasIndex(m => new { m.TenantId, m.ProjectId, m.LogDate })
            .IsDescending(false, false, true);

        // §4.3's per-WbsNode scope read (Tier 1 matching) - filtered because most log rows carry a
        // real WbsNodeId, but a project-level/unattributed row (NULL) is legitimate (§4.1).
        builder.HasIndex(m => new { m.TenantId, m.ProjectId, m.WbsNodeId, m.LogDate })
            .HasFilter("[WbsNodeId] IS NOT NULL");

        // §4.3's direct-ActivityId scope read, when genuinely known.
        builder.HasIndex(m => new { m.TenantId, m.ProjectId, m.ActivityId, m.LogDate })
            .HasFilter("[ActivityId] IS NOT NULL");

        // §4.7 chain-integrity rule 2 - the load-bearing one: "at most one entry may point at any
        // given entry". Filtered (most rows are Original and carry a null CorrectsLogId, which a
        // plain unique index would otherwise collide on) - the authoritative backstop behind
        // RecordManpowerLogCorrectionCommandHandler's own pre-check (409 ManpowerLogAlreadySuperseded).
        builder.HasIndex(m => new { m.TenantId, m.CorrectsLogId })
            .IsUnique()
            .HasFilter("[CorrectsLogId] IS NOT NULL");

        builder.ToTable(tb =>
        {
            tb.HasCheckConstraint("CK_ManpowerEquipmentLogs_WorkerCount", "[WorkerCount] >= 0");
            tb.HasCheckConstraint("CK_ManpowerEquipmentLogs_ManHours", "[ManHours] >= 0");
            tb.HasCheckConstraint("CK_ManpowerEquipmentLogs_OvertimeHours", "[OvertimeHours] >= 0 AND [OvertimeHours] <= [ManHours]");
            tb.HasCheckConstraint("CK_ManpowerEquipmentLogs_EquipmentCount", "[EquipmentCount] >= 0");
            tb.HasCheckConstraint("CK_ManpowerEquipmentLogs_EquipmentOperatingHours", "[EquipmentOperatingHours] >= 0");
            tb.HasCheckConstraint("CK_ManpowerEquipmentLogs_EquipmentStandbyHours", "[EquipmentStandbyHours] >= 0");
        });

        // Append-only (§4.7): no update/delete path exists on the entity (no mutator methods/
        // setters) or here in configuration. Structurally enforced by AppendOnlyGuardInterceptor via
        // the IAppendOnly marker (security review sprint-09.md M-01 pattern) - see the entity's own
        // class remarks.
    }
}
