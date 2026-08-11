using CMPlus.Domain.Enums;

namespace CMPlus.Application.Features.Approval.Queries.GetApprovalPolicyVersionHistory;

/// <summary>
/// One row of a tenant-wide <c>ApprovalPolicy</c>'s version timeline (S15-BE-01). Assembled purely
/// from two already-existing tables - <c>ApprovalPolicy</c> itself (every version is a real,
/// never-deleted row: only <c>IsActive</c>/<c>EffectiveTo</c> flip on <c>Deactivate</c>) and
/// <c>AuditLog</c> (already written for every Create/Update by the standing
/// <c>AuditSaveChangesInterceptor</c>) - per the DoD's explicit "no new storage". The scalar policy
/// fields (<see cref="Version"/>, <see cref="IsActive"/>, <see cref="EffectiveFrom"/>/
/// <see cref="EffectiveTo"/>, the escalation config, <see cref="RuleCount"/>) come straight off
/// <c>ApprovalPolicy</c>; <see cref="CreatedByUserId"/>/<see cref="CreatedAt"/> and
/// <see cref="LastModifiedByUserId"/>/<see cref="LastModifiedAt"/> exist ONLY in <c>AuditLog</c> -
/// <c>ApprovalPolicy</c> itself carries no "who/when" columns at all, which is exactly why the DoD
/// asks for the two sources combined rather than either alone.
///
/// <para>The audit-derived fields are nullable, not merely defensively - a policy version created by
/// a path that bypasses <c>AuditSaveChangesInterceptor</c> (the tenant-provisioning seeder,
/// <c>ApprovalPolicySeeder</c>, runs at tenant-creation time and may not go through the same
/// interceptor pipeline as an ordinary request) genuinely has no matching audit row, and that must
/// read as "unknown", never as a fabricated value or a thrown exception.</para>
/// </summary>
public sealed record ApprovalPolicyVersionHistoryEntryDto(
    Guid ApprovalPolicyId,
    int Version,
    bool IsActive,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    bool AllowSelfApproval,
    decimal? CumulativeVoEscalationPct,
    UserRole? CumulativeVoEscalationRole,
    int RuleCount,
    Guid? CreatedByUserId,
    DateTimeOffset? CreatedAt,
    Guid? LastModifiedByUserId,
    DateTimeOffset? LastModifiedAt);
