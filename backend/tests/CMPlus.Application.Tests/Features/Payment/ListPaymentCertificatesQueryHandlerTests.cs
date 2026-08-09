using CMPlus.Application.Features.Payment.Queries.ListPaymentCertificates;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Tests.Features.Payment;

/// <summary>
/// S9 read-side gap closure (finding L-04): `GET /api/v1/projects/{projectId}/payment-certificates`.
/// </summary>
public class ListPaymentCertificatesQueryHandlerTests
{
    private static PaymentCertificate Draft(Guid tenantId, Guid projectId, int milestoneNo, decimal amount = 1_000_000m) =>
        new(tenantId, projectId, milestoneNo, $"IPC {milestoneNo}", amount, 0m, Guid.NewGuid());

    [Fact]
    public async Task Returns_Only_Certificates_Belonging_To_The_Requested_Project()
    {
        var tenantId = Guid.NewGuid();
        var projectA = Guid.NewGuid();
        var projectB = Guid.NewGuid();
        var repository = new FakePaymentCertificateRepository();
        var inProjectA = Draft(tenantId, projectA, 1);
        var alsoInProjectA = Draft(tenantId, projectA, 2);
        var inProjectB = Draft(tenantId, projectB, 1);
        repository.Seed(inProjectA);
        repository.Seed(alsoInProjectA);
        repository.Seed(inProjectB);

        var handler = new ListPaymentCertificatesQueryHandler(repository);
        var result = await handler.Handle(new ListPaymentCertificatesQuery(projectA), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
        Assert.All(result.Value, dto => Assert.Equal(projectA, dto.ProjectId));
        Assert.DoesNotContain(result.Value, dto => dto.Id == inProjectB.Id);
    }

    [Fact]
    public async Task Returns_Newest_First()
    {
        // Entity.Id is a UUIDv7 (time-ordered by construction, Entity.cs) - the second-created
        // certificate must sort before the first in a "newest first" list. Guid.CreateVersion7()
        // has only millisecond resolution and, empirically verified, no monotonic counter for two
        // values minted within the same millisecond (its sub-millisecond bits are plain random per
        // RFC 9562 - confirmed by a throwaway probe: two ids created back-to-back with no delay
        // disagree with creation order roughly 80% of the time). The 5ms sleep forces the two
        // creations to land in different milliseconds so this test is deterministic; it is not
        // masking a bug in the ordering *comparison* (FakePaymentCertificateRepository/
        // PaymentCertificateRepository both order by the correct RFC-order-preserving ordinal hex
        // string) but working around Guid.CreateVersion7()'s own documented sub-millisecond
        // randomness, which no comparison strategy can fix. See this task's handoff report for why
        // that same limitation is not fixed with a dedicated CreatedAt column in this change.
        var tenantId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var repository = new FakePaymentCertificateRepository();
        var first = Draft(tenantId, projectId, 1);
        Thread.Sleep(5);
        var second = Draft(tenantId, projectId, 2);
        repository.Seed(first);
        repository.Seed(second);

        var handler = new ListPaymentCertificatesQueryHandler(repository);
        var result = await handler.Handle(new ListPaymentCertificatesQuery(projectId), CancellationToken.None);

        Assert.Equal(second.Id, result.Value[0].Id);
        Assert.Equal(first.Id, result.Value[1].Id);
    }

    [Fact]
    public async Task An_Unknown_Project_Yields_An_Empty_List_Not_An_Error()
    {
        // Established precedent for list reads in this codebase (ListEvmSnapshotsQueryHandler,
        // GetJobHistoryAsync): the tenant/project scoping already makes an empty result the correct,
        // non-leaking answer for "wrong tenant", "wrong project" and "does not exist" alike.
        var repository = new FakePaymentCertificateRepository();
        var handler = new ListPaymentCertificatesQueryHandler(repository);

        var result = await handler.Handle(new ListPaymentCertificatesQuery(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public async Task Maps_ApprovalSteps_And_AllowSelfApproval_But_Leaves_Quorum_Progress_Null()
    {
        var tenantId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var repository = new FakePaymentCertificateRepository();
        var certificate = Draft(tenantId, projectId, 1, 5_000_000m);
        certificate.SetPeriodClaim(100m, null, null, 5_000_000m, 0m, 0m, 5_000_000m);
        IReadOnlyList<PaymentCertificateApprovalStepInput> steps =
            [new(1, UserRole.QS, 1), new(2, UserRole.PM, 1)];
        certificate.Submit(steps, Guid.NewGuid(), 1, allowSelfApproval: true, Guid.NewGuid(), DateTimeOffset.UtcNow);
        repository.Seed(certificate);

        var handler = new ListPaymentCertificatesQueryHandler(repository);
        var result = await handler.Handle(new ListPaymentCertificatesQuery(projectId), CancellationToken.None);

        var dto = Assert.Single(result.Value);
        Assert.True(dto.AllowSelfApproval);
        Assert.Equal(2, dto.ApprovalSteps.Count);
        Assert.Equal(UserRole.QS, dto.ApprovalSteps[0].RequiredRole);
        Assert.Equal(UserRole.PM, dto.ApprovalSteps[1].RequiredRole);
        // Deliberately not computed for the list read (ListPaymentCertificatesQueryHandler's own
        // remarks: would require either N+1 or a batched second query, more than this gap closure
        // needs today) - null, not a fabricated 0.
        Assert.Null(dto.CurrentStepApprovalsCollected);
    }
}
