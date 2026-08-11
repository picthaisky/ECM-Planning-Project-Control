using CMPlus.Application.Features.Approval.Queries.GetApprovalActionHistory;
using CMPlus.Application.Features.Payment.Commands.Approve;
using CMPlus.Application.Features.Payment.Commands.RecordPayment;
using CMPlus.Application.Features.Payment.Commands.Reject;
using CMPlus.Application.Features.Payment.Commands.ReturnForRevision;
using CMPlus.Application.Features.Payment.Commands.Submit;
using CMPlus.Application.Features.Payment.Queries.GetPaymentCertificate;
using CMPlus.Domain.Enums;
using CMPlus.WebApi.ErrorHandling;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CMPlus.WebApi.Controllers.Payment;

/// <summary>
/// S9-BE-05: the Payment Certificate approval-chain endpoints (design.md §2.3), plus the S9
/// read-side gap closure (finding L-04): <see cref="GetById"/> and
/// <see cref="GetApprovalActions"/>. Every action requires an authenticated tenant user
/// (<c>[Authorize]</c> at the controller level) - <see cref="Submit"/>/<see cref="RecordPayment"/>
/// and the two reads below add the static <c>QS/PM/ProjectDirector/Admin</c> role gate
/// (<see cref="CertificateCrudRoles"/>), matching <c>ActualCostsController</c>'s established
/// "certificate CRUD surface" pattern - a certificate exposes gross/retention/advance/net,
/// contractor commercial data, so the reads are gated the same way the mutating lifecycle actions
/// already are, not left at bare-authenticated per design.md's general "tenant from JWT claim only"
/// baseline.
///
/// <para><b><see cref="Approve"/>/<see cref="ReturnForRevision"/>/<see cref="Reject"/> deliberately
/// carry no <c>Roles=</c> restriction beyond authentication.</b> Their authority is resolved
/// entirely from the document's version-pinned approval chain inside the command handler
/// (ADR-0008) - a policy can legitimately require any <see cref="UserRole"/> at any step (including
/// one outside the CRUD list above, e.g. <see cref="UserRole.Executive"/>), so a static role
/// attribute here would silently become a second, contradictory gate that could lock out a
/// legitimate approver the policy engine itself would allow. This is also what makes "no PM escape
/// hatch" true structurally: nothing on this controller ever grants authority by role name, only
/// the resolved chain does.</para>
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/payment-certificates")]
public sealed class PaymentCertificatesController(ISender sender) : ControllerBase
{
    /// <summary>
    /// <c>internal</c> (not <c>private</c>) so <see cref="ProjectPaymentCertificatesController"/>'s
    /// project-scoped list read can reuse this exact role set (this task's explicit instruction: "match
    /// the certificate CRUD role set already on PaymentCertificatesController rather than inventing a
    /// new list") without a second, independently-maintained copy of the same string - a
    /// visibility-only change, the role string itself is byte-for-byte unchanged.
    /// </summary>
    internal const string CertificateCrudRoles =
        $"{nameof(UserRole.QS)},{nameof(UserRole.PM)},{nameof(UserRole.ProjectDirector)},{nameof(UserRole.Admin)}";

    /// <summary>
    /// `GET /api/v1/payment-certificates/{id}` (finding L-04) - single-certificate read; see
    /// <see cref="GetPaymentCertificateQuery"/>'s own remarks. A cross-tenant/nonexistent id maps to
    /// a bare 404 (<c>PaymentCertificateNotFound</c>, the same code/mapping <see cref="Submit"/> and
    /// friends already use) via <see cref="ResultProblemMapper"/> - never a 403, never a
    /// differently-shaped body.
    /// </summary>
    [HttpGet("{id:guid}")]
    [Authorize(Roles = CertificateCrudRoles)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetPaymentCertificateQuery(id), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ResultProblemMapper.ToActionResult(result.Error, HttpContext.Request.Path);
    }

    /// <summary>
    /// `GET /api/v1/payment-certificates/{id}/approval-actions` (design.md §2.3; finding L-04) - the
    /// append-only approval-history ledger for this certificate; thin wrapper around the generic
    /// <see cref="GetApprovalActionHistoryQuery"/> (document-type-agnostic, reused unchanged by
    /// Sprint 10's VariationOrder controller). Same 404 discipline as <see cref="GetById"/>: the
    /// query itself checks the certificate exists in the caller's tenant before returning history, so
    /// a cross-tenant/nonexistent id is a bare 404, not a misleading `200` with an empty array.
    /// </summary>
    [HttpGet("{id:guid}/approval-actions")]
    [Authorize(Roles = CertificateCrudRoles)]
    public async Task<IActionResult> GetApprovalActions(Guid id, CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new GetApprovalActionHistoryQuery(ApprovalDocumentType.PaymentCertificate, id), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ResultProblemMapper.ToActionResult(result.Error, HttpContext.Request.Path);
    }

    [HttpPost("{id:guid}/submit")]
    [Authorize(Roles = CertificateCrudRoles)]
    public async Task<IActionResult> Submit(Guid id, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new SubmitPaymentCertificateCommand(id), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ResultProblemMapper.ToActionResult(result.Error, HttpContext.Request.Path);
    }

    [HttpPost("{id:guid}/approve")]
    public async Task<IActionResult> Approve(Guid id, [FromBody] ApprovalCommentRequest request, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new ApprovePaymentCertificateCommand(id, request.Comment), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ResultProblemMapper.ToActionResult(result.Error, HttpContext.Request.Path);
    }

    [HttpPost("{id:guid}/return-for-revision")]
    public async Task<IActionResult> ReturnForRevision(Guid id, [FromBody] ApprovalCommentRequest request, CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new ReturnPaymentCertificateForRevisionCommand(id, request.Comment ?? string.Empty), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ResultProblemMapper.ToActionResult(result.Error, HttpContext.Request.Path);
    }

    [HttpPost("{id:guid}/reject")]
    public async Task<IActionResult> Reject(Guid id, [FromBody] ApprovalCommentRequest request, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new RejectPaymentCertificateCommand(id, request.Comment ?? string.Empty), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ResultProblemMapper.ToActionResult(result.Error, HttpContext.Request.Path);
    }

    [HttpPost("{id:guid}/record-payment")]
    [Authorize(Roles = CertificateCrudRoles)]
    public async Task<IActionResult> RecordPayment(Guid id, [FromBody] RecordPaymentRequest request, CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new RecordPaymentForPaymentCertificateCommand(id, request.Reference, request.PaidAt), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ResultProblemMapper.ToActionResult(result.Error, HttpContext.Request.Path);
    }
}
