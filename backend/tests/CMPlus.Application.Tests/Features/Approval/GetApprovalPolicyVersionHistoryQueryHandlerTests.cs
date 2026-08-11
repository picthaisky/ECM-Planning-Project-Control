using CMPlus.Application.Abstractions;
using CMPlus.Application.Features.Approval.Queries.GetApprovalPolicyVersionHistory;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Tests.Features.Approval;

/// <summary>
/// S15-BE-01: this handler is a thin pass-through onto <see cref="IApprovalPolicyHistoryReader"/> -
/// the interesting "compose AuditLog + ApprovalPolicy.Version" logic is proven at the Infrastructure/
/// Integration level (<c>ApprovalPolicyHistoryReaderTests</c>). This file only pins the handler's own
/// two contract points: it never turns an empty history into a failure, and it returns whatever the
/// reader hands back unchanged.
/// </summary>
public class GetApprovalPolicyVersionHistoryQueryHandlerTests
{
    private sealed class FakeApprovalPolicyHistoryReader : IApprovalPolicyHistoryReader
    {
        public List<ApprovalPolicyVersionHistoryEntryDto> Entries { get; } = [];

        public Task<IReadOnlyList<ApprovalPolicyVersionHistoryEntryDto>> GetVersionHistoryAsync(
            ApprovalDocumentType documentType, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ApprovalPolicyVersionHistoryEntryDto>>(Entries);
    }

    [Fact]
    public async Task An_Empty_History_Is_A_Success_With_An_Empty_List_Not_A_Failure()
    {
        var handler = new GetApprovalPolicyVersionHistoryQueryHandler(new FakeApprovalPolicyHistoryReader());

        var result = await handler.Handle(
            new GetApprovalPolicyVersionHistoryQuery(ApprovalDocumentType.VariationOrder), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public async Task Returns_The_Readers_Entries_Unchanged()
    {
        var reader = new FakeApprovalPolicyHistoryReader();
        var entry = new ApprovalPolicyVersionHistoryEntryDto(
            Guid.NewGuid(), 2, true, DateTimeOffset.UtcNow.AddDays(-1), null, false, 10.00m, UserRole.Executive, 6,
            Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(-1), null, null);
        reader.Entries.Add(entry);

        var handler = new GetApprovalPolicyVersionHistoryQueryHandler(reader);
        var result = await handler.Handle(
            new GetApprovalPolicyVersionHistoryQuery(ApprovalDocumentType.VariationOrder), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Same(entry, Assert.Single(result.Value));
    }
}
