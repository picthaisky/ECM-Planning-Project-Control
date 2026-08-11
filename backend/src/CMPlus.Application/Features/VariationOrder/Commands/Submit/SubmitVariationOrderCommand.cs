using CMPlus.Domain.Common;
using MediatR;

namespace CMPlus.Application.Features.VariationOrder.Commands.Submit;

/// <summary>
/// <c>[Draft] --Submit--&gt; [PendingApproval(step 1)]</c> (domain-rules.md §2.3). S10-BE-02: resolves
/// the approval chain via the Sprint 2 <c>IApprovalRoutingService</c> - reused as-is, never rewritten
/// (S10-BE-02 DoD: "ใช้ service เดิมของ Sprint 2 ไม่เขียนใหม่") - and snapshots the resolved chain onto
/// the document (H-01 fix pattern). An empty resolved chain, or a configured escalation threshold with
/// no usable baseline, fails closed (422 <c>ApprovalPolicyGap</c>/<c>ContractValueNotConfigured</c>) -
/// never auto-approves.
/// </summary>
public sealed record SubmitVariationOrderCommand(Guid VariationOrderId) : IRequest<Result<VariationOrderDto>>;
