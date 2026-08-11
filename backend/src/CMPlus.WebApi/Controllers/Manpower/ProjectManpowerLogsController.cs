using CMPlus.Application.Features.Manpower;
using CMPlus.Application.Features.Manpower.Commands.RecordManpowerLog;
using CMPlus.Application.Features.Manpower.Commands.RecordManpowerLogCorrection;
using CMPlus.Application.Features.Manpower.Queries.GetProductivityIndex;
using CMPlus.Domain.Enums;
using CMPlus.WebApi.ErrorHandling;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CMPlus.WebApi.Controllers.Manpower;

/// <summary>
/// S12-BE-02 (US-12.2): manpower/equipment log endpoints per domain-rules.md (manpower-equipment)
/// §4.7's two-write-route design - <c>POST .../manpower-logs</c> (Original only) and
/// <c>POST .../manpower-logs/{logId}/corrections</c> (Correction/Retraction) - the identical shape
/// <c>ProjectWeatherLogsController</c> established. There is deliberately no update/delete action:
/// <see cref="Put"/>/<see cref="Patch"/>/<see cref="Delete"/> below are explicit routes that always
/// answer <b>405</b> with a <c>ManpowerLogIsImmutable</c> ProblemDetails pointing at the corrections
/// endpoint.
///
/// <para><b>Role-gating decision.</b> <see cref="WriteRoles"/> reuses
/// <c>ProjectWeatherLogsController.WriteRoles</c>/<c>ProjectPhotosController.WriteRoles</c>'s
/// established "who records field data" role set exactly (PM/Planning/Site/QS/Admin) - this is now
/// the third site-data-capture write path in this codebase to land on that same set. §4.8's own
/// ruling: "no approval chain - the approval engine routes on a money value, and this entity has
/// none", so there is no separate approve/reject surface to gate. Reads (including the Productivity
/// Index) are gated only by <see cref="AuthorizeAttribute"/> with no role restriction, mirroring
/// <c>ProjectWeatherLogsController</c>'s identical "read wider than write" reasoning - PDPA-light
/// counts-only data (§4.9), not commercially sensitive the way cost/payment data is.</para>
/// </summary>
[ApiController]
[Route("api/v1/projects/{projectId:guid}/manpower-logs")]
public sealed class ProjectManpowerLogsController(ISender sender) : ControllerBase
{
    internal const string WriteRoles =
        $"{nameof(UserRole.PM)},{nameof(UserRole.Planning)},{nameof(UserRole.Site)},{nameof(UserRole.QS)},{nameof(UserRole.Admin)}";

    [HttpPost]
    [Authorize(Roles = WriteRoles)]
    public async Task<IActionResult> Record(
        Guid projectId, [FromBody] RecordManpowerLogRequest request, CancellationToken cancellationToken)
    {
        var command = new RecordManpowerLogCommand(
            projectId,
            request.LogDate,
            request.Shift,
            request.WorkCategoryId,
            request.WbsNodeId,
            request.ActivityId,
            request.LabourType,
            request.SubcontractorRef,
            request.WorkerCount,
            request.ManHours,
            request.OvertimeHours,
            request.ManHoursDerived,
            request.EquipmentCount,
            request.EquipmentOperatingHours,
            request.EquipmentStandbyHours,
            request.WorkDescription,
            request.RelatedWeatherLogId,
            request.AllowDuplicate);

        var result = await sender.Send(command, cancellationToken);

        return result.IsSuccess
            ? Created($"/api/v1/projects/{projectId}/manpower-logs", result.Value)
            : ResultProblemMapper.ToActionResult(result.Error, HttpContext.Request.Path);
    }

    [HttpPost("{logId:guid}/corrections")]
    [Authorize(Roles = WriteRoles)]
    public async Task<IActionResult> RecordCorrection(
        Guid projectId, Guid logId, [FromBody] RecordManpowerLogCorrectionRequest request, CancellationToken cancellationToken)
    {
        var command = new RecordManpowerLogCorrectionCommand(
            projectId,
            logId, // always from the route (§4.7) - the request body carries no CorrectsLogId field at all.
            request.EntryKind,
            request.CorrectionReason,
            request.LogDate,
            request.Shift,
            request.WorkCategoryId,
            request.WbsNodeId,
            request.ActivityId,
            request.LabourType,
            request.SubcontractorRef,
            request.WorkerCount,
            request.ManHours,
            request.OvertimeHours,
            request.ManHoursDerived,
            request.EquipmentCount,
            request.EquipmentOperatingHours,
            request.EquipmentStandbyHours,
            request.WorkDescription,
            request.RelatedWeatherLogId);

        var result = await sender.Send(command, cancellationToken);

        return result.IsSuccess
            ? Created($"/api/v1/projects/{projectId}/manpower-logs", result.Value)
            : ResultProblemMapper.ToActionResult(result.Error, HttpContext.Request.Path);
    }

    /// <summary>§5's Productivity Index read (S12-FE-02's consumer) - <c>wbsNodeId</c>/<c>activityId</c>
    /// narrow the scope (§4.3 Tier 1; omit both for the whole project), <c>from</c> omitted means a
    /// cumulative read from project start (§5.2's bottom formula, the KPI tile), <c>to</c> is
    /// required (the half-open period's inclusive upper bound, §4.5).</summary>
    [HttpGet("productivity-index")]
    [Authorize]
    public async Task<IActionResult> GetProductivityIndex(
        Guid projectId,
        [FromQuery] Guid? wbsNodeId,
        [FromQuery] Guid? activityId,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetProductivityIndexQuery(projectId, wbsNodeId, activityId, from, to), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ResultProblemMapper.ToActionResult(result.Error, HttpContext.Request.Path);
    }

    /// <summary>domain-rules.md §4.7's deliberate 405 (defence layer 1 of 3 - see the Domain entity's
    /// remarks for layers 2/3). Any authenticated user may reach this action; the response itself
    /// never depends on role, since the whole point is "this verb does not exist here at all", not an
    /// authorization decision.</summary>
    [HttpPut("{logId:guid}")]
    [Authorize]
    public IActionResult Put(Guid projectId, Guid logId) => ManpowerLogIsImmutable();

    [HttpPatch("{logId:guid}")]
    [Authorize]
    public IActionResult Patch(Guid projectId, Guid logId) => ManpowerLogIsImmutable();

    [HttpDelete("{logId:guid}")]
    [Authorize]
    public IActionResult Delete(Guid projectId, Guid logId) => ManpowerLogIsImmutable();

    private IActionResult ManpowerLogIsImmutable() =>
        ResultProblemMapper.ToActionResult(ManpowerLogErrorCodes.IsImmutable, HttpContext.Request.Path);
}
