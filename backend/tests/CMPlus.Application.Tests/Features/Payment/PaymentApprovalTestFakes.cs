using CMPlus.Application.Abstractions;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Tests.Features.Payment;

/// <summary>Shared, in-memory <c>Result</c>-free test doubles for the S9-BE-05 Payment Certificate
/// approval command handler unit tests - reused across Submit/Approve/ReturnForRevision/Reject/
/// RecordPayment's own test classes so each does not hand-roll its own copy.</summary>
internal sealed class FakePaymentCertificateRepository : IPaymentCertificateRepository
{
    private readonly Dictionary<Guid, PaymentCertificate> _certificates = [];

    public List<ProjectFinanceLedger> AddedLedgerEntries { get; } = [];

    public bool SaveShouldSucceed { get; set; } = true;

    public int SaveCallCount { get; private set; }

    public void Seed(PaymentCertificate certificate) => _certificates[certificate.Id] = certificate;

    public Task<PaymentCertificate?> FindAsync(Guid paymentCertificateId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_certificates.GetValueOrDefault(paymentCertificateId));

    public void AddLedgerEntries(IReadOnlyList<ProjectFinanceLedger> entries) => AddedLedgerEntries.AddRange(entries);

    public Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveCallCount++;
        return Task.FromResult(SaveShouldSucceed);
    }

    public Task<IReadOnlyList<PaymentCertificate>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        // Ordinal string comparison, matching the real PaymentCertificateRepository's ordering
        // exactly - see that type's remarks on why plain Guid.CompareTo (OrderByDescending(c => c.Id))
        // does not reliably reflect UUIDv7 creation order.
        IReadOnlyList<PaymentCertificate> rows = _certificates.Values
            .Where(c => c.ProjectId == projectId)
            .OrderByDescending(c => c.Id.ToString(), StringComparer.Ordinal)
            .ToList();

        return Task.FromResult(rows);
    }

    public Task<PaymentCertificate?> GetByIdAsync(Guid paymentCertificateId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_certificates.GetValueOrDefault(paymentCertificateId));
}

internal sealed class FakeApprovalActionRepository : IApprovalActionRepository
{
    public List<ApprovalAction> Actions { get; } = [];

    public void Add(ApprovalAction action) => Actions.Add(action);

    public Task<IReadOnlyList<ApprovalAction>> GetHistoryAsync(
        ApprovalDocumentType documentType, Guid documentId, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ApprovalAction> history = Actions
            .Where(a => a.DocumentType == documentType && a.DocumentId == documentId)
            .OrderBy(a => a.ActedAt)
            .ToList();

        return Task.FromResult(history);
    }
}

internal sealed class FakeApprovalPolicyReaderForPayment : IApprovalPolicyReader
{
    public List<ApprovalPolicy> Policies { get; } = [];

    public Task<IReadOnlyList<ApprovalPolicy>> GetCandidatePoliciesAsync(
        ApprovalDocumentType documentType, DateTimeOffset asOf, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ApprovalPolicy> candidates = Policies
            .Where(p => p.DocumentType == documentType && p.IsActive
                        && p.EffectiveFrom <= asOf && (p.EffectiveTo is null || p.EffectiveTo >= asOf))
            .ToList();

        return Task.FromResult(candidates);
    }

    public Task<ApprovalPolicy?> GetActiveTenantDefaultPolicyAsync(
        ApprovalDocumentType documentType, CancellationToken cancellationToken = default)
    {
        var policy = Policies
            .Where(p => p.DocumentType == documentType && p.ProjectId == null && p.IsActive)
            .OrderByDescending(p => p.Version)
            .FirstOrDefault();

        return Task.FromResult(policy);
    }

    public Task<ApprovalPolicy?> GetByIdAsync(Guid approvalPolicyId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Policies.FirstOrDefault(p => p.Id == approvalPolicyId));
}

internal sealed class FakeTenantProviderForPayment(Guid tenantId) : ITenantProvider
{
    public Guid TenantId { get; } = tenantId;
}

/// <summary>
/// <paramref name="userId"/> is deliberately <see cref="Nullable{T}"/> (widened from a plain
/// <c>Guid</c> - every existing call site passes a concrete <c>Guid</c>, which converts implicitly,
/// so this is source-compatible with all of them) so sprint-10 security review L-01's "fail closed on
/// a null actor" guards are actually testable: <c>new FakeCurrentUserContextForPayment(null, role)</c>
/// reproduces <c>ICurrentUserContext.UserId is null</c>, the one case none of this codebase's tests
/// exercised before this fix.
/// </summary>
internal sealed class FakeCurrentUserContextForPayment(Guid? userId, UserRole role) : ICurrentUserContext
{
    public Guid? UserId { get; } = userId;

    public UserRole Role { get; } = role;
}

internal sealed class FakeClockForPayment(DateTimeOffset now) : IDateTimeProvider
{
    public DateTimeOffset UtcNow { get; } = now;
}
