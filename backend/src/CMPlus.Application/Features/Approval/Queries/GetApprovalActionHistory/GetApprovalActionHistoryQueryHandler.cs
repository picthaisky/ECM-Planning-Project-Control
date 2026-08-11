using CMPlus.Application.Abstractions;
using CMPlus.Application.Features.Payment;
using CMPlus.Domain.Common;
using CMPlus.Domain.Enums;
using MediatR;

namespace CMPlus.Application.Features.Approval.Queries.GetApprovalActionHistory;

/// <summary>
/// Checks the referenced document actually exists (in the caller's tenant) before returning its
/// history, rather than relying solely on <see cref="ApprovalAction"/>'s own tenant scoping - a
/// cross-tenant/nonexistent id must produce a bare 404 (security review sprint-09.md's IDOR
/// discipline: "wrong tenant" and "does not exist" indistinguishable), not a `200` with an empty
/// array that would also be indistinguishable from "this real, same-tenant certificate simply has no
/// history yet" (e.g. still `Draft`, never submitted). <see cref="PaymentApprovalErrorCodes.NotFound"/>
/// is reused as-is (mapped to 404 by the same <c>ResultProblemMapper</c> entry the five S9-BE-05
/// mutating commands already use) rather than inventing a differently-named code, per this task's
/// explicit instruction.
///
/// <para>The existence check is necessarily per-<see cref="ApprovalDocumentType"/> - "does a
/// document with this id exist" has a different answer for each aggregate - so it is the one part of
/// this otherwise-generic handler that is not yet fully document-type-agnostic. Sprint 10 added the
/// <see cref="ApprovalDocumentType.VariationOrder"/> arm alongside its aggregate.</para>
///
/// <para><b>Lesson worth keeping.</b> Before that arm existed, the <c>_ =&gt; false</c> default made
/// <c>GET /variation-orders/{id}/approval-actions</c> return 404 <i>unconditionally</i> - the route,
/// the controller and the shared DTO were all real and shipped, so nothing failed to compile and no
/// backend test noticed. It was caught only when <c>frontend-developer</c> tried to consume the
/// endpoint. A fail-closed default is the right choice here, but it converts "unimplemented" into a
/// response that is indistinguishable from a legitimate 404, so any new document type must add its
/// arm in the same change that adds its routes.</para>
/// </summary>
public sealed class GetApprovalActionHistoryQueryHandler(
    IApprovalActionRepository approvalActions,
    IPaymentCertificateRepository paymentCertificates,
    IVariationOrderRepository variationOrders)
    : IRequestHandler<GetApprovalActionHistoryQuery, Result<IReadOnlyList<ApprovalActionDto>>>
{
    public async Task<Result<IReadOnlyList<ApprovalActionDto>>> Handle(
        GetApprovalActionHistoryQuery request, CancellationToken cancellationToken)
    {
        var documentExists = await DocumentExistsAsync(request.DocumentType, request.DocumentId, cancellationToken);
        if (!documentExists)
        {
            return Result<IReadOnlyList<ApprovalActionDto>>.Failure(PaymentApprovalErrorCodes.NotFound);
        }

        var history = await approvalActions.GetHistoryAsync(request.DocumentType, request.DocumentId, cancellationToken);

        return Result<IReadOnlyList<ApprovalActionDto>>.Success(history.Select(ApprovalActionDto.From).ToList());
    }

    private async Task<bool> DocumentExistsAsync(ApprovalDocumentType documentType, Guid documentId, CancellationToken cancellationToken) =>
        documentType switch
        {
            ApprovalDocumentType.PaymentCertificate =>
                await paymentCertificates.GetByIdAsync(documentId, cancellationToken) is not null,
            ApprovalDocumentType.VariationOrder =>
                await variationOrders.GetByIdAsync(documentId, cancellationToken) is not null,
            // Any future document type falls through to "not found" rather than throwing, which is
            // the fail-closed direction. Note this arm silently made the whole VO history endpoint
            // return 404 unconditionally between S10-BE-01 landing the aggregate and this arm being
            // added - a stale `_ => false` is indistinguishable from a real 404 at the API surface,
            // so a new document type must add its arm here in the same change that adds its routes.
            _ => false,
        };
}
