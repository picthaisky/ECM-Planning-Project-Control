using CMPlus.Application.Features.Approval.Queries.GetApprovalActionHistory;
using CMPlus.Application.Features.VariationOrder.Commands.Approve;
using CMPlus.Application.Features.VariationOrder.Commands.Cancel;
using CMPlus.Application.Features.VariationOrder.Commands.Reject;
using CMPlus.Application.Features.VariationOrder.Commands.ReturnForRevision;
using CMPlus.Application.Features.VariationOrder.Commands.Submit;
using CMPlus.Application.Features.VariationOrder.Commands.UpdateContent;
using CMPlus.Application.Features.VariationOrder.Commands.Withdraw;
using CMPlus.Application.Features.VariationOrder.Queries.GetVariationOrder;
using CMPlus.Domain.Enums;
using CMPlus.WebApi.ErrorHandling;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CMPlus.WebApi.Controllers.VariationOrder;

/// <summary>
/// S10-BE-01/02/03: the Variation Order id-scoped endpoints - mirrors
/// <c>CMPlus.WebApi.Controllers.Payment.PaymentCertificatesController</c>'s shape exactly, including
/// its most important structural property: <see cref="Approve"/>/<see cref="Reject"/>/
/// <see cref="ReturnForRevision"/> carry NO static <c>Roles=</c> restriction beyond authentication.
/// Their authority is resolved entirely from the document's version-pinned approval chain inside the
/// command handler (ADR-0008) - a policy can legitimately require any <see cref="UserRole"/> at any
/// step (including <see cref="UserRole.Executive"/> once the domain-rules.md §4 cumulative-VO
/// escalation rung is present), so a static role attribute here would silently become a second,
/// contradictory gate. <see cref="Withdraw"/>/<see cref="Cancel"/> ARE exposed (domain-rules.md §2.3's
/// explicit note that Sprint 10 must expose both, unlike Sprint 9's still-unreachable IPC pair, N-02).
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/variation-orders")]
public sealed class VariationOrdersController(ISender sender) : ControllerBase
{
    /// <summary>
    /// <c>internal</c> (not <c>private</c>) so <see cref="ProjectVariationOrdersController"/>'s
    /// project-scoped list/create can reuse this exact role set - the same visibility-only sharing
    /// <c>PaymentCertificatesController.CertificateCrudRoles</c> already established for its sibling
    /// project-scoped controller. A VO is typically raised by Planning/QS/PM and reviewed up through
    /// ProjectDirector; Admin is included for the same tenant-operations reason every other CRUD role
    /// set in this codebase includes it.
    /// </summary>
    internal const string VoCrudRoles =
        $"{nameof(UserRole.PM)},{nameof(UserRole.Planning)},{nameof(UserRole.QS)},{nameof(UserRole.ProjectDirector)},{nameof(UserRole.Admin)}";

    /// <summary>
    /// Read access is <see cref="VoCrudRoles"/> <b>plus <see cref="UserRole.Executive"/></b>, because
    /// an Executive can be pulled into a VO's chain by cumulative escalation (ADR-0015) without ever
    /// being a VO author. Gating reads on <see cref="VoCrudRoles"/> alone produced a real gap found
    /// by <c>frontend-developer</c>: the escalated approver could call <c>approve</c> - which carries
    /// no static role gate, by design, since authority comes from the pinned chain - but got 403 on
    /// <c>GET</c> for the very document they were being asked to sign. Escalation exists precisely to
    /// bring an Executive in, so denying them sight of the document defeats the mechanism and invites
    /// blind approval, which is worse than the access it was protecting.
    ///
    /// <para>Deliberately <b>not</b> extended to the write actions: an Executive should not be
    /// raising, re-pricing, submitting or cancelling VOs. Read and write diverge here on purpose.</para>
    /// </summary>
    internal const string VoReadRoles = $"{VoCrudRoles},{nameof(UserRole.Executive)}";

    [HttpGet("{id:guid}")]
    [Authorize(Roles = VoReadRoles)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetVariationOrderQuery(id), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ResultProblemMapper.ToActionResult(result.Error, HttpContext.Request.Path);
    }

    [HttpGet("{id:guid}/approval-actions")]
    [Authorize(Roles = VoReadRoles)]
    public async Task<IActionResult> GetApprovalActions(Guid id, CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new GetApprovalActionHistoryQuery(ApprovalDocumentType.VariationOrder, id), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ResultProblemMapper.ToActionResult(result.Error, HttpContext.Request.Path);
    }

    [HttpPut("{id:guid}/content")]
    [Authorize(Roles = VoCrudRoles)]
    public async Task<IActionResult> UpdateContent(Guid id, [FromBody] UpdateVariationOrderContentRequest request, CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new UpdateVariationOrderContentCommand(
                id, request.Description, request.Justification, request.Amount, request.TimeImpactDays, request.ScopeItems),
            cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ResultProblemMapper.ToActionResult(result.Error, HttpContext.Request.Path);
    }

    [HttpPost("{id:guid}/submit")]
    [Authorize(Roles = VoCrudRoles)]
    public async Task<IActionResult> Submit(Guid id, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new SubmitVariationOrderCommand(id), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ResultProblemMapper.ToActionResult(result.Error, HttpContext.Request.Path);
    }

    [HttpPost("{id:guid}/approve")]
    public async Task<IActionResult> Approve(Guid id, [FromBody] VoApprovalCommentRequest request, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new ApproveVariationOrderCommand(id, request.Comment), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ResultProblemMapper.ToActionResult(result.Error, HttpContext.Request.Path);
    }

    [HttpPost("{id:guid}/return-for-revision")]
    public async Task<IActionResult> ReturnForRevision(Guid id, [FromBody] VoApprovalCommentRequest request, CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new ReturnVariationOrderForRevisionCommand(id, request.Comment ?? string.Empty), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ResultProblemMapper.ToActionResult(result.Error, HttpContext.Request.Path);
    }

    [HttpPost("{id:guid}/reject")]
    public async Task<IActionResult> Reject(Guid id, [FromBody] VoApprovalCommentRequest request, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new RejectVariationOrderCommand(id, request.Comment ?? string.Empty), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ResultProblemMapper.ToActionResult(result.Error, HttpContext.Request.Path);
    }

    [HttpPost("{id:guid}/withdraw")]
    [Authorize(Roles = VoCrudRoles)]
    public async Task<IActionResult> Withdraw(Guid id, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new WithdrawVariationOrderCommand(id), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ResultProblemMapper.ToActionResult(result.Error, HttpContext.Request.Path);
    }

    [HttpPost("{id:guid}/cancel")]
    [Authorize(Roles = VoCrudRoles)]
    public async Task<IActionResult> Cancel(Guid id, [FromBody] VoApprovalCommentRequest request, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new CancelVariationOrderCommand(id, request.Comment ?? string.Empty), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ResultProblemMapper.ToActionResult(result.Error, HttpContext.Request.Path);
    }
}
