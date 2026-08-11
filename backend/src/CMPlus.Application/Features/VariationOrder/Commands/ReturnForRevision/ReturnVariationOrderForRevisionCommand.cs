using CMPlus.Domain.Common;
using MediatR;

namespace CMPlus.Application.Features.VariationOrder.Commands.ReturnForRevision;

/// <summary>
/// <c>[PendingApproval] --ReturnForRevision--&gt; [Draft]</c>: any pending step's role holder, not
/// quorum-bound (domain-rules.md §2.3/§8.4 - deliberately the deadlock-free escape valve).
/// <paramref name="Comment"/> is mandatory.
/// </summary>
public sealed record ReturnVariationOrderForRevisionCommand(Guid VariationOrderId, string Comment) : IRequest<Result<VariationOrderDto>>;
