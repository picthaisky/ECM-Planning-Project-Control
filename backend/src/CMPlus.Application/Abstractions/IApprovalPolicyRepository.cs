using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Abstractions;

/// <summary>
/// Write-side persistence boundary for the Tenant Admin approval-policy editor (S9-BE-06),
/// deliberately separate from the read-only <see cref="IApprovalPolicyReader"/> (S2-BE-05/07) the
/// same way <c>IProjectRepository</c> (write) is separate from <c>IProjectReader</c> (read) - this
/// interface returns a <b>tracked</b> entity because <see cref="ApprovalPolicy.Deactivate"/> is a
/// mutation that must be persisted, which <see cref="IApprovalPolicyReader"/>'s <c>AsNoTracking</c>
/// reads are unsuitable for.
/// </summary>
public interface IApprovalPolicyRepository
{
    /// <summary>Tracked (not <c>AsNoTracking</c>). The tenant-wide (<see cref="ApprovalPolicy.ProjectId"/>
    /// <see langword="null"/>) currently-active policy for <paramref name="documentType"/>, or
    /// <see langword="null"/> if the tenant has none yet (the very first
    /// <c>PUT .../approval-policies/{documentType}</c> for a document type creates
    /// <see cref="ApprovalPolicy.CreateInitialVersion"/> rather than a next version). Tenant-scoped
    /// by the global EF query filter (ADR-0002).</summary>
    Task<ApprovalPolicy?> FindActiveTenantDefaultAsync(
        ApprovalDocumentType documentType, CancellationToken cancellationToken = default);

    /// <summary>Stages a brand-new <see cref="ApprovalPolicy"/> row (either
    /// <see cref="ApprovalPolicy.CreateInitialVersion"/> or the result of
    /// <see cref="ApprovalPolicy.CreateNextVersion"/>) - never an edit of an existing row
    /// (approval-workflow.md §5.2 "version-pin, never mutate").</summary>
    void AddVersion(ApprovalPolicy policy);

    /// <summary>
    /// Persists the staged new version and, when <see cref="FindActiveTenantDefaultAsync"/> returned
    /// a previous version that was subsequently <see cref="ApprovalPolicy.Deactivate"/>d, that
    /// deactivation - both in the same atomic <c>SaveChanges</c> call, so a reader can never
    /// observe two simultaneously-active versions of the same policy in the ordinary, single-request
    /// case.
    ///
    /// <para><b>Same-shape concurrent-request race as <c>IBaselineRepository.TryActivateAsync</c>
    /// (see that method's remarks for the full mechanism) - found during the Baseline fix's own
    /// review, since <see cref="ApprovalPolicyConfiguration"/> ships the identical
    /// `(TenantId, ProjectId, DocumentType) WHERE IsActive = 1` filtered-unique-index shape.</b> Two
    /// concurrent <c>PUT .../approval-policies/{documentType}</c> requests can each call
    /// <see cref="FindActiveTenantDefaultAsync"/> and observe the same pre-race active policy before
    /// either commits, then each stage a different <c>nextVersion</c> via <see cref="AddVersion"/> -
    /// whichever commits second collides with the first's already-committed active row on the real
    /// unique index. Returns <see langword="false"/> (never lets the raw
    /// <see cref="Microsoft.EntityFrameworkCore.DbUpdateException"/> escape) when this specific
    /// collision - classified by <c>Infrastructure.Persistence.UniqueIndexViolationClassifier</c>,
    /// the same helper <c>BaselineRepository.TryActivateAsync</c> uses - or a genuine
    /// <see cref="Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException"/> occurs;
    /// <see langword="true"/> on an ordinary successful save. Any other
    /// <see cref="Microsoft.EntityFrameworkCore.DbUpdateException"/> shape still propagates unhandled,
    /// unchanged - see that classifier's remarks for why a bare
    /// <c>catch (DbUpdateException)</c> would be unsafe.</para>
    ///
    /// <para><b>ADR-0021, closed 2026-08-11 (Sprint 15 approval-policy hardening):</b> this used to
    /// NOT close the race for every <see cref="ApprovalPolicy"/> that exists today, because every
    /// policy <c>UpdateApprovalPolicyCommandHandler</c> actually creates has
    /// <see cref="ApprovalPolicy.ProjectId"/> <see langword="null"/> (tenant-wide default is the only
    /// exposed write surface - project-scoped override is schema-present but "not surfaced until a
    /// later sprint", see that property's own remarks), and standard SQL unique-index semantics
    /// (ANSI, not a SQLite quirk) treat NULL as never equal to another NULL, so the old single index
    /// `(TenantId, ProjectId, DocumentType) WHERE IsActive = 1` provided <b>no protection whatsoever</b>
    /// when both competing rows had <c>ProjectId = null</c>. <see cref="Configurations.ApprovalPolicyConfiguration"/>
    /// now ships <b>two</b> filtered unique indexes split on <c>ProjectId</c> nullability - `(TenantId,
    /// DocumentType) WHERE IsActive = 1 AND ProjectId IS NULL` and `(TenantId, ProjectId, DocumentType)
    /// WHERE IsActive = 1 AND ProjectId IS NOT NULL` - so every index's key columns are non-null
    /// wherever its filter applies and the classifier above now catches the null-<c>ProjectId</c> race
    /// too, exactly like the non-null case. Proven directly in
    /// <c>ApprovalPolicyActivationConcurrencySqliteTests.Tenant_Wide_Default_Policy_ProjectId_Is_Null_The_Split_Index_Now_Rejects_A_Second_Simultaneously_Active_Version</c>,
    /// with a red-first mutation-proof companion test reproducing the old, broken behaviour against a
    /// locally-replicated pre-fix schema in the same test run.</para>
    /// </summary>
    Task<bool> SaveChangesAsync(CancellationToken cancellationToken = default);
}
