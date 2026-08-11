using CMPlus.Domain.Common;
using MediatR;

namespace CMPlus.Application.Features.VariationOrder.Commands.Reject;

/// <summary>
/// <c>[PendingApproval] --Reject--&gt; [Rejected]</c> (terminal): only the final step's approver may
/// reject, quorum-bound identically to Approve from day one (ADR-0016). <paramref name="Comment"/> is
/// mandatory (domain-rules.md §2.3).
/// </summary>
public sealed record RejectVariationOrderCommand(Guid VariationOrderId, string Comment) : IRequest<Result<VariationOrderDto>>;
