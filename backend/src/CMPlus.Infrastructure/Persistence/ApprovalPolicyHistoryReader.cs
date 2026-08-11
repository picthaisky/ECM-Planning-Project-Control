using CMPlus.Application.Abstractions;
using CMPlus.Application.Features.Approval.Queries.GetApprovalPolicyVersionHistory;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CMPlus.Infrastructure.Persistence;

/// <summary>
/// S15-BE-01: <see cref="IApprovalPolicyHistoryReader"/> against <see cref="CmPlusDbContext"/>.
/// Composes the version timeline from two already-existing, already-tenant-scoped tables only -
/// <see cref="CmPlusDbContext.ApprovalPolicies"/> (every version, active or superseded - a
/// <c>Deactivate</c> never deletes a row) and <see cref="CmPlusDbContext.AuditLogs"/> (already
/// written for every Create/Update by <c>AuditSaveChangesInterceptor</c>) - no new table, per the
/// DoD. Both DbSets carry the standard <c>ITenantOwned</c> global query filter (ADR-0002), so no
/// query here filters by TenantId explicitly.
/// </summary>
public sealed class ApprovalPolicyHistoryReader(CmPlusDbContext dbContext) : IApprovalPolicyHistoryReader
{
    public async Task<IReadOnlyList<ApprovalPolicyVersionHistoryEntryDto>> GetVersionHistoryAsync(
        ApprovalDocumentType documentType, CancellationToken cancellationToken = default)
    {
        var versions = await dbContext.ApprovalPolicies
            .AsNoTracking()
            .Include(p => p.Rules)
            .Where(p => p.DocumentType == documentType && p.ProjectId == null)
            .OrderBy(p => p.Version)
            .ToListAsync(cancellationToken);

        if (versions.Count == 0)
        {
            return [];
        }

        var policyIds = versions.Select(p => p.Id).ToList();

        // One round trip for every version's audit trail, rather than N+1 - AuditLogs already carries
        // the (TenantId, EntityName, EntityId) index this filter serves (AuditLogConfiguration).
        var auditRows = await dbContext.AuditLogs
            .AsNoTracking()
            .Where(a => a.EntityName == nameof(ApprovalPolicy) && policyIds.Contains(a.EntityId))
            .OrderBy(a => a.Timestamp)
            .ToListAsync(cancellationToken);

        var auditByPolicyId = auditRows.ToLookup(a => a.EntityId);

        return versions
            .Select(policy =>
            {
                var policyAudit = auditByPolicyId[policy.Id];
                var created = policyAudit.FirstOrDefault(a => a.Action == AuditAction.Created);
                // A policy row is expected to receive at most one Update in its lifetime (the single
                // Deactivate() call an UpdateApprovalPolicyCommand's NEXT version triggers on this
                // one) - OrderByDescending().FirstOrDefault() stays correct even so, and degrades
                // gracefully to "the most recent one" if that assumption is ever broken.
                var lastModified = policyAudit
                    .Where(a => a.Action == AuditAction.Updated)
                    .OrderByDescending(a => a.Timestamp)
                    .FirstOrDefault();

                return new ApprovalPolicyVersionHistoryEntryDto(
                    policy.Id,
                    policy.Version,
                    policy.IsActive,
                    policy.EffectiveFrom,
                    policy.EffectiveTo,
                    policy.AllowSelfApproval,
                    policy.CumulativeVoEscalationPct,
                    policy.CumulativeVoEscalationRole,
                    policy.Rules.Count,
                    created?.UserId,
                    created?.Timestamp,
                    lastModified?.UserId,
                    lastModified?.Timestamp);
            })
            .ToList();
    }
}
