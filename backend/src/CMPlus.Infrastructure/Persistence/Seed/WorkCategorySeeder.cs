using CMPlus.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CMPlus.Infrastructure.Persistence.Seed;

/// <summary>
/// Seeds a standard tenant-wide (<see cref="WorkCategory.ProjectId"/> = null) work-category catalogue
/// for every newly-provisioned tenant, so the Man/Equipment log form can offer a real dropdown
/// instead of a raw GUID field (the S12 catalogue gap). Tenant-wide, so every project in the tenant
/// sees it (<c>IManpowerEquipmentLogRepository.FindExistingWorkCategoryIdsAsync</c> accepts
/// <c>ProjectId == null || ProjectId == projectId</c>). Idempotent: checks each <c>Code</c> before
/// inserting, so re-running against an already-seeded tenant creates nothing new - the same
/// convention <see cref="ApprovalPolicySeeder"/> and <see cref="DevDataSeeder"/>'s get-or-create
/// helpers follow. Wired into <see cref="DevDataSeeder"/>'s tenant-creation path (dev/CI seeding and
/// real tenant provisioning share it, S1 baseline).
/// </summary>
public static class WorkCategorySeeder
{
    /// <summary>A standard Thai construction work-breakdown catalogue (human-approved 2026-08-13 as the
    /// default set). Code is the natural key; DisplayOrder follows list order.</summary>
    private static readonly IReadOnlyList<(string Code, string NameTh, string NameEn)> DefaultCategories =
    [
        ("GEN", "งานทั่วไป / งานเตรียมการ", "General & Preliminaries"),
        ("STR", "งานโครงสร้าง", "Structural"),
        ("ARC", "งานสถาปัตยกรรม", "Architectural"),
        ("MEP", "งานระบบไฟฟ้าและเครื่องกล", "Mechanical, Electrical & Plumbing"),
        ("SAN", "งานสุขาภิบาล", "Sanitary"),
        ("FIN", "งานตกแต่งและงานสี", "Finishing"),
        ("EXT", "งานภายนอกและภูมิทัศน์", "External Works & Landscape"),
    ];

    public static async Task SeedDefaultCategoriesAsync(
        CmPlusDbContext dbContext, Guid tenantId, CancellationToken cancellationToken = default)
    {
        var displayOrder = 1;
        foreach (var (code, nameTh, nameEn) in DefaultCategories)
        {
            // Scoped to the active tenant by the global query filter (ADR-0002); Code is unique per
            // (TenantId) among the tenant-wide entries, matching the IX_WorkCategories_TenantId_Code index.
            var exists = await dbContext.WorkCategories
                .AnyAsync(w => w.ProjectId == null && w.Code == code, cancellationToken);

            if (!exists)
            {
                dbContext.WorkCategories.Add(new WorkCategory(tenantId, projectId: null, code, nameTh, nameEn, displayOrder));
            }

            displayOrder++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
