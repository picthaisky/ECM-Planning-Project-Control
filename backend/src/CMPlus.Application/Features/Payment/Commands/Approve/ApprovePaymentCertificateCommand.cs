using CMPlus.Domain.Common;
using MediatR;

namespace CMPlus.Application.Features.Payment.Commands.Approve;

/// <summary>
/// <c>[PendingApproval] --Approve--&gt;</c> same state (<c>CurrentStepNo+1</c>) or
/// <c>[Certified]</c> once the chain is exhausted (approval-workflow.md §4). Authority is resolved
/// entirely from the document's version-pinned chain - there is deliberately no role short-circuit
/// of any kind (S9-BE-05 DoD: "no escape hatch such as 'PM can always approve'").
/// <paramref name="Comment"/> is optional on Approve (approval-workflow.md §4 rule 2).
/// </summary>
public sealed record ApprovePaymentCertificateCommand(Guid PaymentCertificateId, string? Comment)
    : IRequest<Result<PaymentCertificateDto>>;
