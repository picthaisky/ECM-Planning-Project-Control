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
/// this otherwise-generic handler that is not yet fully document-type-agnostic.
/// <see cref="ApprovalDocumentType.VariationOrder"/> has no aggregate at all yet (lands Sprint 10,
/// ADR-0008), so by definition no such document exists to have a history; that arm intentionally
/// returns "not found" rather than throwing, so calling this query with <c>VariationOrder</c> today
/// degrades to a correct 404 instead of a crash. Sprint 10 adds one case arm here (plus its own
/// document-existence check and, if the generic 404 code's name is judged misleading for a VO,
/// its own not-found code) - not a redesign.</para>
/// </summary>
public sealed class GetApprovalActionHistoryQueryHandler(
    IApprovalActionRepository approvalActions, IPaymentCertificateRepository paymentCertificates)
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
            // ApprovalDocumentType.VariationOrder: no aggregate exists yet (Sprint 10) - see this
            // type's remarks.
            _ => false,
        };
}
