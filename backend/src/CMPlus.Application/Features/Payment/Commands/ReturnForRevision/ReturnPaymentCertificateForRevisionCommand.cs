using CMPlus.Domain.Common;
using MediatR;

namespace CMPlus.Application.Features.Payment.Commands.ReturnForRevision;

/// <summary>
/// <c>[PendingApproval] --ReturnForRevision--&gt; [Draft]</c> (approval-workflow.md §4): bumps
/// <c>RevisionNo</c>, voids every approval collected on this revision (no partial carry-over -
/// §6.3), and unfreezes money fields. Guard: the actor holds <i>any</i> pending step's role (not
/// only the current one) - distinct from <c>Approve</c>/<c>Reject</c>. <paramref name="Comment"/>
/// is mandatory (approval-workflow.md §4 rule 2).
/// </summary>
public sealed record ReturnPaymentCertificateForRevisionCommand(Guid PaymentCertificateId, string Comment)
    : IRequest<Result<PaymentCertificateDto>>;
