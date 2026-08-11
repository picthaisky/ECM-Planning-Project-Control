using CMPlus.Application.Features.Weather;
using CMPlus.Application.Features.Weather.Commands.RecordWeatherLog;
using CMPlus.Application.Features.Weather.Commands.RecordWeatherLogCorrection;
using CMPlus.Application.Features.Weather.Queries.ListWeatherLogs;
using CMPlus.Domain.Enums;
using CMPlus.WebApi.ErrorHandling;
using CMPlus.WebApi.Middleware;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CMPlus.WebApi.Controllers.Weather;

/// <summary>
/// S11-BE-01 (US-11.1): weather log endpoints per domain-rules.md weather-eot §8.3's two-write-route
/// design - <c>POST .../weather-logs</c> (Original only) and
/// <c>POST .../weather-logs/{logId}/corrections</c> (Correction/Retraction). There is deliberately
/// no update/delete action: <see cref="Put"/>/<see cref="Patch"/>/<see cref="Delete"/> below are not
/// omitted oversights - they are explicit routes that always answer <b>405</b> with a
/// <c>WeatherLogIsImmutable</c> ProblemDetails pointing at the corrections endpoint (§8.3: "better
/// than a bare routing 404/405, because the field user needs to be told what to do instead").
///
/// <para><b>Role-gating decision.</b> <see cref="WriteRoles"/> (the two <c>POST</c> actions) mirrors
/// <c>ProgressController</c>/<c>ImportController.ProgressImportRoles</c>'s established "who records
/// field data" role set exactly - PM/Planning/Site/QS/Admin - because this task's brief is explicit
/// that Site users record daily weather in practice, and this codebase already has two precedents
/// for that exact role set on an analogous site-data-capture write path. Reads are gated only by
/// <see cref="AuthorizeAttribute"/> with no role restriction at all, mirroring
/// <c>WbsController</c>'s bare read gate: weather data is not commercially sensitive the way cost/
/// payment data is (contrast <c>ActualCostsController</c>'s QS/PM/ProjectDirector/Admin-only gate),
/// so read is deliberately WIDER than write - the opposite divergence direction from
/// <c>VariationOrdersController.VoReadRoles</c> (widened for one specific escalation-visibility
/// reason): here writing tamper-evident legal evidence should be narrower than reading it.</para>
/// </summary>
[ApiController]
[Route("api/v1/projects/{projectId:guid}/weather-logs")]
public sealed class ProjectWeatherLogsController(ISender sender) : ControllerBase
{
    internal const string WriteRoles =
        $"{nameof(UserRole.PM)},{nameof(UserRole.Planning)},{nameof(UserRole.Site)},{nameof(UserRole.QS)},{nameof(UserRole.Admin)}";

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> List(
        Guid projectId, [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new ListWeatherLogsQuery(projectId, from, to), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ResultProblemMapper.ToActionResult(result.Error, HttpContext.Request.Path);
    }

    [HttpPost]
    [Authorize(Roles = WriteRoles)]
    [Idempotent] // S13-BE-01: S13-FE-01 adds this kind to the outbox this sprint - see IdempotencyMiddleware's class remarks.
    public async Task<IActionResult> Record(
        Guid projectId, [FromBody] RecordWeatherLogRequest request, CancellationToken cancellationToken)
    {
        var command = new RecordWeatherLogCommand(
            projectId,
            request.LogDate,
            request.Condition,
            request.ConditionNote,
            request.RainfallMm,
            request.Impact,
            request.ImpactNote,
            request.HoursLost,
            request.AffectedActivityIds ?? []);

        var result = await sender.Send(command, cancellationToken);

        return result.IsSuccess
            ? Created($"/api/v1/projects/{projectId}/weather-logs", result.Value)
            : ResultProblemMapper.ToActionResult(result.Error, HttpContext.Request.Path);
    }

    [HttpPost("{logId:guid}/corrections")]
    [Authorize(Roles = WriteRoles)]
    [Idempotent] // S13-BE-01: same write-shape as Record above - see IdempotencyMiddleware's class remarks.
    public async Task<IActionResult> RecordCorrection(
        Guid projectId, Guid logId, [FromBody] RecordWeatherLogCorrectionRequest request, CancellationToken cancellationToken)
    {
        var command = new RecordWeatherLogCorrectionCommand(
            projectId,
            logId, // always from the route (§8.3) - the request body carries no CorrectsWeatherLogId field at all.
            request.EntryKind,
            request.CorrectionReason,
            request.LogDate,
            request.Condition,
            request.ConditionNote,
            request.RainfallMm,
            request.Impact,
            request.ImpactNote,
            request.HoursLost,
            request.AffectedActivityIds ?? []);

        var result = await sender.Send(command, cancellationToken);

        return result.IsSuccess
            ? Created($"/api/v1/projects/{projectId}/weather-logs", result.Value)
            : ResultProblemMapper.ToActionResult(result.Error, HttpContext.Request.Path);
    }

    /// <summary>domain-rules.md §8.3's deliberate 405 (defence layer 1 of 3 - see the Domain
    /// entity's remarks for layers 2/3). Any authenticated user may reach this action; the response
    /// itself never depends on role, since the whole point is "this verb does not exist here at
    /// all", not an authorization decision.</summary>
    [HttpPut("{logId:guid}")]
    [Authorize]
    public IActionResult Put(Guid projectId, Guid logId) => WeatherLogIsImmutable();

    [HttpPatch("{logId:guid}")]
    [Authorize]
    public IActionResult Patch(Guid projectId, Guid logId) => WeatherLogIsImmutable();

    [HttpDelete("{logId:guid}")]
    [Authorize]
    public IActionResult Delete(Guid projectId, Guid logId) => WeatherLogIsImmutable();

    private IActionResult WeatherLogIsImmutable() =>
        ResultProblemMapper.ToActionResult(WeatherLogErrorCodes.IsImmutable, HttpContext.Request.Path);
}
