using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CMPlus.Infrastructure.Persistence.Seed;

/// <summary>
/// Seeds the two default tenant-wide policies - <c>TH-Default-VO</c> and <c>TH-Default-IPC</c> -
/// with the exact rule bands from approval-workflow.md §8, for every newly-provisioned tenant
/// (ADR-0008/S2-BE-06). Wired into <see cref="DevDataSeeder"/>'s tenant-creation path today
/// (dev/CI seeding and "real" tenant provisioning are the same code path per Sprint 1); a future
/// dedicated tenant-onboarding flow should call this too. Idempotent: checks for an existing
/// active policy of the same <see cref="ApprovalDocumentType"/> before creating one, so re-running
/// against an already-seeded tenant creates nothing new (same convention as
/// <see cref="DevDataSeeder"/>'s other Get-or-create helpers).
/// </summary>
public static class ApprovalPolicySeeder
{
    /// <summary>
    /// approval-workflow.md §8 / ADR-0015: cumulative-VO escalation, when configured, appends
    /// <see cref="UserRole.Executive"/> once approved-VO drift exceeds the tenant's chosen threshold.
    /// <see cref="ApprovalPolicy.ProjectId"/> is left null (tenant-wide default) - project-scoped
    /// overrides are a Sprint 15 surface (ADR-0008 consequences).
    /// </summary>
    private static readonly IReadOnlyList<ApprovalPolicyRuleInput> VoRules =
    [
        new(StepNo: 1, MinAmount: 0.00m, MaxAmount: 500_000.00m, RequiredRole: UserRole.PM),
        new(StepNo: 1, MinAmount: 500_000.00m, MaxAmount: 5_000_000.00m, RequiredRole: UserRole.PM),
        new(StepNo: 2, MinAmount: 500_000.00m, MaxAmount: 5_000_000.00m, RequiredRole: UserRole.ProjectDirector),
        new(StepNo: 1, MinAmount: 5_000_000.00m, MaxAmount: null, RequiredRole: UserRole.PM),
        new(StepNo: 2, MinAmount: 5_000_000.00m, MaxAmount: null, RequiredRole: UserRole.ProjectDirector),
        new(StepNo: 3, MinAmount: 5_000_000.00m, MaxAmount: null, RequiredRole: UserRole.Executive),
    ];

    /// <summary>approval-workflow.md §8: QS + PM always required; ProjectDirector joins above
    /// 10,000,000.00 (R7/R8 fixtures).</summary>
    private static readonly IReadOnlyList<ApprovalPolicyRuleInput> IpcRules =
    [
        new(StepNo: 1, MinAmount: 0.00m, MaxAmount: null, RequiredRole: UserRole.QS),
        new(StepNo: 2, MinAmount: 0.00m, MaxAmount: null, RequiredRole: UserRole.PM),
        new(StepNo: 3, MinAmount: 10_000_000.00m, MaxAmount: null, RequiredRole: UserRole.ProjectDirector),
    ];

    public static async Task SeedDefaultPoliciesAsync(
        CmPlusDbContext dbContext, Guid tenantId, DateTimeOffset effectiveFrom, CancellationToken cancellationToken = default)
    {
        // ADR-0015 (human-confirmed 2026-08-10: "Threshold default 10.00%") / sprint-10 security
        // review M-03: seed 10.00, not NULL. domain-rules.md §4.5 still reads "PENDING - no default
        // set" because it predates the human's answer; the ADR is the later, authoritative source and
        // wins per this codebase's ADR-precedence rule. Shipping NULL here left cumulative-VO
        // escalation switched OFF in every tenant with no signal distinguishing "deliberately
        // disabled" from "provisioning never turned it on" - execution-verified in the review: a
        // project 41.9% varied approved another VO with no Executive signature anywhere.
        // CumulativeVoEscalationRole stays Executive, as it already was.
        await GetOrCreateAsync(
            dbContext, tenantId, ApprovalDocumentType.VariationOrder, effectiveFrom,
            VoRules, cumulativeVoEscalationPct: 10.00m, cumulativeVoEscalationRole: UserRole.Executive,
            cancellationToken);

        await GetOrCreateAsync(
            dbContext, tenantId, ApprovalDocumentType.PaymentCertificate, effectiveFrom,
            IpcRules, cumulativeVoEscalationPct: null, cumulativeVoEscalationRole: null,
            cancellationToken);
    }

    private static async Task GetOrCreateAsync(
        CmPlusDbContext dbContext,
        Guid tenantId,
        ApprovalDocumentType documentType,
        DateTimeOffset effectiveFrom,
        IReadOnlyList<ApprovalPolicyRuleInput> rules,
        decimal? cumulativeVoEscalationPct,
        UserRole? cumulativeVoEscalationRole,
        CancellationToken cancellationToken)
    {
        // Scoped by the active tenant via the global query filter (ADR-0002), matching the
        // (TenantId, ProjectId, DocumentType) filtered-unique-active index.
        var alreadyExists = await dbContext.ApprovalPolicies
            .AnyAsync(p => p.DocumentType == documentType && p.ProjectId == null && p.IsActive, cancellationToken);

        if (alreadyExists)
        {
            return;
        }

        var policy = ApprovalPolicy.CreateInitialVersion(
            tenantId, projectId: null, documentType, effectiveFrom, rules,
            allowSelfApproval: false, cumulativeVoEscalationPct, cumulativeVoEscalationRole);

        dbContext.ApprovalPolicies.Add(policy);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
