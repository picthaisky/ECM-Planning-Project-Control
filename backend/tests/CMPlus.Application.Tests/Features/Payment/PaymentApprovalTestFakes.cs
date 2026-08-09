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

internal sealed class FakeCurrentUserContextForPayment(Guid userId, UserRole role) : ICurrentUserContext
{
    public Guid? UserId { get; } = userId;

    public UserRole Role { get; } = role;
}

internal sealed class FakeClockForPayment(DateTimeOffset now) : IDateTimeProvider
{
    public DateTimeOffset UtcNow { get; } = now;
}
