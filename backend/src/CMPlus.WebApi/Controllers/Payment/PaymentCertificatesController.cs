using CMPlus.Application.Features.Payment.Commands.Approve;
using CMPlus.Application.Features.Payment.Commands.RecordPayment;
using CMPlus.Application.Features.Payment.Commands.Reject;
using CMPlus.Application.Features.Payment.Commands.ReturnForRevision;
using CMPlus.Application.Features.Payment.Commands.Submit;
using CMPlus.Domain.Enums;
using CMPlus.WebApi.ErrorHandling;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CMPlus.WebApi.Controllers.Payment;

/// <summary>
/// S9-BE-05: the Payment Certificate approval-chain endpoints (design.md §2.3). Every action
/// requires an authenticated tenant user (<c>[Authorize]</c> at the controller level) - only
/// <see cref="Submit"/>/<see cref="RecordPayment"/> add the static
/// <c>QS/PM/ProjectDirector/Admin</c> role gate, matching <c>ActualCostsController</c>'s
/// established "certificate CRUD surface" pattern for the document's own lifecycle actions.
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
    private const string CertificateCrudRoles =
        $"{nameof(UserRole.QS)},{nameof(UserRole.PM)},{nameof(UserRole.ProjectDirector)},{nameof(UserRole.Admin)}";

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
