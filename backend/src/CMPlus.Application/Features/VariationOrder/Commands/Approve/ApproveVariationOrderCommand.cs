using CMPlus.Domain.Common;
using MediatR;

namespace CMPlus.Application.Features.VariationOrder.Commands.Approve;

/// <summary>
/// <c>[PendingApproval] --Approve--&gt;</c> same state (<c>CurrentStepNo+1</c>) or <c>[Approved]</c>
/// (terminal) once the chain is exhausted. S10-BE-03: when this vote exhausts the chain, also runs the
/// domain-rules.md §4.7 escalation-bypass re-check and, on success, moves
/// <c>Project.BAC</c>/<c>Project.ContractValue</c> (signed) and the scope payload's activity budget
/// deltas, all in one unit of work. <paramref name="Comment"/> is optional.
/// </summary>
public sealed record ApproveVariationOrderCommand(Guid VariationOrderId, string? Comment) : IRequest<Result<VariationOrderDto>>;
