using CMPlus.Application.Abstractions;
using CMPlus.Domain.Common;
using MediatR;

namespace CMPlus.Application.Features.Approval.Queries.GetApprovalPolicyVersionHistory;

/// <summary>
/// Thin pass-through onto <see cref="IApprovalPolicyHistoryReader"/> - all the "compose AuditLog +
/// ApprovalPolicy.Version, no new storage" work lives in that reader's Infrastructure implementation,
/// mirroring <c>GetApprovalPolicyQueryHandler</c>'s own shape. An empty list is a legitimate,
/// successful answer (never configured yet, or a cross-tenant request the global query filter has
/// already emptied) - there is no "not found" failure case here, deliberately: unlike a specific
/// policy id lookup, "the history of this document type" always has a well-defined (possibly empty)
/// answer.
/// </summary>
public sealed class GetApprovalPolicyVersionHistoryQueryHandler(IApprovalPolicyHistoryReader reader)
    : IRequestHandler<GetApprovalPolicyVersionHistoryQuery, Result<IReadOnlyList<ApprovalPolicyVersionHistoryEntryDto>>>
{
    public async Task<Result<IReadOnlyList<ApprovalPolicyVersionHistoryEntryDto>>> Handle(
        GetApprovalPolicyVersionHistoryQuery request, CancellationToken cancellationToken)
    {
        var history = await reader.GetVersionHistoryAsync(request.DocumentType, cancellationToken);
        return Result<IReadOnlyList<ApprovalPolicyVersionHistoryEntryDto>>.Success(history);
    }
}
