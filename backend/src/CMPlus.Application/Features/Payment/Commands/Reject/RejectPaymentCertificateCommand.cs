using CMPlus.Domain.Common;
using MediatR;

namespace CMPlus.Application.Features.Payment.Commands.Reject;

/// <summary>
/// <c>[PendingApproval] --Reject--&gt; [Rejected]</c> (terminal, approval-workflow.md §4): only the
/// <b>final</b> step's approver may reject - intermediate approvers may only
/// <c>ReturnForRevision</c> (approval-workflow.md §6.1, "mirrors real construction practice where
/// only the ultimate authority refuses"). <paramref name="Comment"/> is mandatory.
/// </summary>
public sealed record RejectPaymentCertificateCommand(Guid PaymentCertificateId, string Comment)
    : IRequest<Result<PaymentCertificateDto>>;
