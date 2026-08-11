using CMPlus.Domain.Common;
using MediatR;

namespace CMPlus.Application.Features.VariationOrder.Commands.Withdraw;

/// <summary>
/// <c>[PendingApproval] --Withdraw--&gt; [Draft]</c>: submitter only, step 1, zero votes cast this
/// revision (domain-rules.md §2.3). Exposed by a controller unlike Sprint 9's IPC (N-02, sprint-09.md
/// §9.5) - domain-rules.md §2.3's note: "Sprint 10 must expose both [Withdraw/Cancel] for the VO".
/// </summary>
public sealed record WithdrawVariationOrderCommand(Guid VariationOrderId) : IRequest<Result<VariationOrderDto>>;
