using CMPlus.Application.Features.Progress.Commands.BatchRecordProgress;
using CMPlus.Domain.Enums;
using CMPlus.WebApi.ErrorHandling;
using CMPlus.WebApi.Middleware;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CMPlus.WebApi.Controllers.Progress;

/// <summary>
/// S4-BE-03 (US-4.5): the WBS &amp; Activity screen's batch "Update Progress" submission.
///
/// <para>Role gate mirrors <c>ImportController</c>'s progress-import role set (S3-SEC-01 M-04) -
/// this is functionally the same "who records field progress" concern, just via a grid instead of
/// an Excel upload.</para>
/// </summary>
[ApiController]
[Authorize(Roles = $"{nameof(UserRole.PM)},{nameof(UserRole.Planning)},{nameof(UserRole.Site)},{nameof(UserRole.QS)},{nameof(UserRole.Admin)}")]
[Route("api/v1/projects/{projectId:guid}/progress")]
public sealed class ProgressController(ISender sender) : ControllerBase
{
    [HttpPost("batch")]
    [Idempotent] // S13-BE-01: S13-FE-01 adds this kind to the outbox this sprint - see IdempotencyMiddleware's class remarks.
    public async Task<IActionResult> RecordBatch(
        Guid projectId, [FromBody] BatchRecordProgressRequest request, CancellationToken cancellationToken)
    {
        var entries = request.Entries
            .Select(e => new BatchProgressEntry(e.ActivityId, e.ProgressPercentage, e.ActualQuantity))
            .ToList();

        var command = new BatchRecordProgressCommand(projectId, request.PeriodEndDate, entries);
        var result = await sender.Send(command, cancellationToken);

        return result.IsSuccess
            ? Ok(result.Value)
            : ResultProblemMapper.ToActionResult(result.Error, HttpContext.Request.Path);
    }
}
