using CMPlus.Application.Features.VariationOrder.Commands.Create;
using CMPlus.Application.Features.VariationOrder.Queries.ListVariationOrders;
using CMPlus.WebApi.ErrorHandling;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CMPlus.WebApi.Controllers.VariationOrder;

/// <summary>
/// `POST /api/v1/projects/{projectId}/variation-orders` (create) and
/// `GET /api/v1/projects/{projectId}/variation-orders` (the project-scoped VO register) - a separate
/// controller class from <see cref="VariationOrdersController"/>, not extra actions on it, mirroring
/// the established <c>WbsController</c>/<c>WbsNodesController</c> and
/// <c>ProjectPaymentCertificatesController</c>/<c>PaymentCertificatesController</c> project-scoped-vs-
/// id-scoped split this codebase already uses.
/// </summary>
[ApiController]
[Authorize(Roles = VariationOrdersController.VoCrudRoles)]
[Route("api/v1/projects/{projectId:guid}/variation-orders")]
public sealed class ProjectVariationOrdersController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(Guid projectId, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new ListVariationOrdersQuery(projectId), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ResultProblemMapper.ToActionResult(result.Error, HttpContext.Request.Path);
    }

    [HttpPost]
    public async Task<IActionResult> Create(Guid projectId, [FromBody] CreateVariationOrderRequest request, CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new CreateVariationOrderCommand(
                projectId, request.VoNumber, request.Description, request.Justification,
                request.Amount, request.TimeImpactDays, request.ScopeItems),
            cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ResultProblemMapper.ToActionResult(result.Error, HttpContext.Request.Path);
    }
}
