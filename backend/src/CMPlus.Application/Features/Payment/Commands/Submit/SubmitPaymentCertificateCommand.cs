using CMPlus.Domain.Common;
using MediatR;

namespace CMPlus.Application.Features.Payment.Commands.Submit;

/// <summary>
/// <c>[Draft] --Submit--&gt; [PendingApproval(step 1)]</c> (approval-workflow.md §4). Resolves the
/// approval chain via <c>IApprovalRoutingService</c> and snapshots the resolved policy
/// version onto the document (ADR-0008) - a later policy edit (S9-BE-06) can never retroactively
/// change how this revision was routed. An empty resolved chain fails closed with
/// <c>ApprovalPolicyGap</c> (422) - never auto-approves (approval-workflow.md §5.3 step 6).
/// </summary>
public sealed record SubmitPaymentCertificateCommand(Guid PaymentCertificateId) : IRequest<Result<PaymentCertificateDto>>;
