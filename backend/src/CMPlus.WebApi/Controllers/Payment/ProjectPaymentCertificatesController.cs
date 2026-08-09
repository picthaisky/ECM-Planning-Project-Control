using CMPlus.Application.Features.Payment.Queries.ListPaymentCertificates;
using CMPlus.WebApi.ErrorHandling;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CMPlus.WebApi.Controllers.Payment;

/// <summary>
/// `GET /api/v1/projects/{projectId}/payment-certificates` (docs/9 §6; S9 read-side gap closure,
/// finding L-04) - the project-scoped milestone/certificate list
/// (`web/src/features/payment/api.ts#listPaymentCertificates`, already built front-end-side and
/// waiting on this endpoint). A separate controller class from <see cref="PaymentCertificatesController"/>,
/// not an extra action on it, because it is a genuinely different route template
/// (project-scoped, not id-scoped) - the same split this codebase already uses for
/// <c>WbsController</c> (project-scoped tree) vs. <c>WbsNodesController</c> (node-scoped actions).
///
/// <para>Role-gated identically to <see cref="PaymentCertificatesController"/>'s own reads
/// (<see cref="PaymentCertificatesController.CertificateCrudRoles"/>: QS/PM/ProjectDirector/Admin) -
/// this task's explicit instruction to reuse the certificate CRUD role set rather than invent a new
/// one for this read.</para>
///
/// <para>Never 404s - see <see cref="ListPaymentCertificatesQueryHandler"/>'s remarks on why an
/// empty list is the correct response for an unknown/cross-tenant/other-project id, matching this
/// codebase's established list-read precedent.</para>
/// </summary>
[ApiController]
[Authorize(Roles = PaymentCertificatesController.CertificateCrudRoles)]
[Route("api/v1/projects/{projectId:guid}/payment-certificates")]
public sealed class ProjectPaymentCertificatesController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(Guid projectId, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new ListPaymentCertificatesQuery(projectId), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ResultProblemMapper.ToActionResult(result.Error, HttpContext.Request.Path);
    }
}
