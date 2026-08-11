using CMPlus.Application.Abstractions;
using CMPlus.Application.Features.Approval;
using CMPlus.Application.Features.Approval.Commands.UpdateApprovalPolicy;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Tests.Features.Approval;

/// <summary>
/// S9-BE-06 DoD: <c>PUT .../approval-policies/{documentType}</c> always creates <c>Version+1</c> and
/// deactivates the previous version - never edits in place; invalid bands (overlapping or gapped)
/// -&gt; 400 identifying the offending <c>StepNo</c>.
/// </summary>
public class UpdateApprovalPolicyCommandHandlerTests
{
    private sealed class FakeApprovalPolicyRepository : IApprovalPolicyRepository
    {
        public ApprovalPolicy? Current { get; set; }

        public List<ApprovalPolicy> AddedVersions { get; } = [];

        public int SaveCallCount { get; private set; }

        /// <summary>Simulates <c>IApprovalPolicyRepository.SaveChangesAsync</c> reporting the
        /// concurrent-request race (S14-BE-01 review follow-up) - set <see langword="false"/> in a
        /// test to exercise <c>UpdateApprovalPolicyCommandHandler</c>'s new
        /// <c>ApprovalPolicyErrorCodes.ConcurrencyConflict</c> branch without a real database.</summary>
        public bool SaveChangesSucceeds { get; set; } = true;

        public Task<ApprovalPolicy?> FindActiveTenantDefaultAsync(ApprovalDocumentType documentType, CancellationToken cancellationToken = default) =>
            Task.FromResult(Current);

        public void AddVersion(ApprovalPolicy policy) => AddedVersions.Add(policy);

        public Task<bool> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCallCount++;
            return Task.FromResult(SaveChangesSucceeds);
        }
    }

    private sealed class FakeTenantProvider(Guid tenantId) : ITenantProvider
    {
        public Guid TenantId { get; } = tenantId;
    }

    private sealed class FixedClock(DateTimeOffset now) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-09T09:00:00+07:00");

    private static (UpdateApprovalPolicyCommandHandler Handler, FakeApprovalPolicyRepository Repository) CreateHandler(Guid? tenantId = null)
    {
        var repository = new FakeApprovalPolicyRepository();
        var handler = new UpdateApprovalPolicyCommandHandler(
            repository, new FakeTenantProvider(tenantId ?? Guid.NewGuid()), new FixedClock(Now));
        return (handler, repository);
    }

    private static readonly IReadOnlyList<ApprovalPolicyRuleInput> ValidIpcRules =
    [
        new(1, 0.00m, null, UserRole.QS),
        new(2, 0.00m, null, UserRole.PM),
        new(3, 10_000_000.00m, null, UserRole.ProjectDirector),
    ];

    [Fact]
    public async Task Handle_Creates_Version_1_When_The_Tenant_Has_No_Existing_Policy()
    {
        var (handler, repository) = CreateHandler();

        var result = await handler.Handle(
            new UpdateApprovalPolicyCommand(ApprovalDocumentType.PaymentCertificate, false, null, null, ValidIpcRules),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Version);
        Assert.True(result.Value.IsActive);
        Assert.Equal(3, result.Value.Rules.Count);
        Assert.Equal(1, repository.SaveCallCount);
        Assert.Single(repository.AddedVersions);
    }

    [Fact]
    public async Task Handle_Creates_Version_Plus_One_And_Deactivates_The_Previous_Version_Never_Editing_In_Place()
    {
        var tenantId = Guid.NewGuid();
        var (handler, repository) = CreateHandler(tenantId);
        var v1 = ApprovalPolicy.CreateInitialVersion(
            tenantId, null, ApprovalDocumentType.PaymentCertificate, Now.AddYears(-1), ValidIpcRules);
        repository.Current = v1;

        var result = await handler.Handle(
            new UpdateApprovalPolicyCommand(
                ApprovalDocumentType.PaymentCertificate, true, 5.00m, UserRole.Executive,
                [new ApprovalPolicyRuleInput(1, 0.00m, null, UserRole.PM)]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Version);
        Assert.True(result.Value.AllowSelfApproval);
        Assert.Equal(5.00m, result.Value.CumulativeVoEscalationPct);

        // v1 itself was never mutated except Deactivate() - a whole new row was staged, never an edit.
        Assert.False(v1.IsActive);
        var addedVersion = Assert.Single(repository.AddedVersions);
        Assert.NotEqual(v1.Id, addedVersion.Id);
        Assert.Equal(2, addedVersion.Version);
        Assert.Equal(1, repository.SaveCallCount);
    }

    [Fact]
    public async Task Handle_Rejects_A_Non_Contiguous_StepNo_Gap_With_400_Identifying_The_Offending_StepNo()
    {
        var (handler, _) = CreateHandler();
        var gappedRules = new List<ApprovalPolicyRuleInput>
        {
            new(1, 0.00m, 500_000.00m, UserRole.PM),
            new(1, 500_000.00m, 5_000_000.00m, UserRole.PM),
            new(3, 500_000.00m, 5_000_000.00m, UserRole.Executive), // StepNo 2 missing
        };

        var result = await handler.Handle(
            new UpdateApprovalPolicyCommand(ApprovalDocumentType.VariationOrder, false, null, null, gappedRules),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.StartsWith("ApprovalPolicyBandGap:", result.Error);
        Assert.Equal("ApprovalPolicyBandGap:2", result.Error);
    }

    [Fact]
    public async Task Handle_Rejects_Two_Rules_For_The_Same_StepNo_With_Intersecting_Ranges_With_400_Identifying_The_StepNo()
    {
        var (handler, repository) = CreateHandler();
        var overlappingRules = new List<ApprovalPolicyRuleInput>
        {
            new(1, 0.00m, 1_000_000.00m, UserRole.PM),
            new(1, 500_000.00m, 2_000_000.00m, UserRole.QS), // same StepNo 1, overlaps [500k,1M)
        };

        var result = await handler.Handle(
            new UpdateApprovalPolicyCommand(ApprovalDocumentType.VariationOrder, false, null, null, overlappingRules),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("ApprovalPolicyBandOverlap:1", result.Error);
        Assert.Empty(repository.AddedVersions); // nothing was ever staged/saved
        Assert.Equal(0, repository.SaveCallCount);
    }

    /// <summary>S14-BE-01 review follow-up: <see cref="IApprovalPolicyRepository.SaveChangesAsync"/>
    /// returning <see langword="false"/> (a genuine concurrent-request race on the filtered unique
    /// index, or - unreachable today, kept for symmetry - an optimistic-concurrency mismatch) must
    /// surface as <see cref="ApprovalPolicyErrorCodes.ConcurrencyConflict"/>, never an unhandled
    /// exception or a false-success response.</summary>
    [Fact]
    public async Task Handle_Returns_ConcurrencyConflict_When_The_Repository_Reports_A_Failed_Save()
    {
        var (handler, repository) = CreateHandler();
        repository.SaveChangesSucceeds = false;

        var result = await handler.Handle(
            new UpdateApprovalPolicyCommand(ApprovalDocumentType.PaymentCertificate, false, null, null, ValidIpcRules),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ApprovalPolicyErrorCodes.ConcurrencyConflict, result.Error);
    }

    [Fact]
    public async Task Handle_Accepts_Two_Rules_For_The_Same_StepNo_With_Adjacent_Non_Overlapping_Ranges()
    {
        // TH-Default-VO's own real shape: StepNo 1 defined three times across disjoint bands - not
        // an overlap, must not be rejected by the new additive check.
        var (handler, _) = CreateHandler();
        var thDefaultVoRules = new List<ApprovalPolicyRuleInput>
        {
            new(1, 0.00m, 500_000.00m, UserRole.PM),
            new(1, 500_000.00m, 5_000_000.00m, UserRole.PM),
            new(2, 500_000.00m, 5_000_000.00m, UserRole.ProjectDirector),
            new(1, 5_000_000.00m, null, UserRole.PM),
            new(2, 5_000_000.00m, null, UserRole.ProjectDirector),
            new(3, 5_000_000.00m, null, UserRole.Executive),
        };

        var result = await handler.Handle(
            new UpdateApprovalPolicyCommand(ApprovalDocumentType.VariationOrder, false, 10.00m, UserRole.Executive, thDefaultVoRules),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(6, result.Value.Rules.Count);
    }
}
