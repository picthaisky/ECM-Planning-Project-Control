using CMPlus.Application.Abstractions;
using CMPlus.Application.Features.Import.Commands.ImportExcelProgress;
using CMPlus.Application.Features.Import.Commands.ImportScheduleFile;
using CMPlus.Application.Features.Import.Queries.ExportProgressTemplate;
using CMPlus.Application.Features.Import.Queries.GetImportJob;
using CMPlus.Application.Features.Import.Queries.GetImportJobHistory;
using CMPlus.Application.Import;
using CMPlus.Domain.Enums;
using CMPlus.WebApi.ErrorHandling;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CMPlus.WebApi.Controllers.Import;

/// <summary>
/// S3-BE-04: XER/MSPDI/Excel import endpoints + job history, per docs/10 §6 Sprint 3
/// (<c>POST /api/v1/projects/{id}/import/{xer|mspdi|xlsx}</c>). Thin per clean-architecture-dotnet -
/// parses the route/form input, dispatches to MediatR, maps the result; every business decision
/// (size cap, parsing, cycle/XXE/formula-injection rejection, bulk persistence) lives in
/// Application/Infrastructure.
///
/// <para><b>S3-SEC-01 finding M-04 - role gate on <see cref="Import"/>.</b> Both routes were
/// previously gated only by <c>[Authorize]</c> (any authenticated user, any of the seven
/// <see cref="UserRole"/> values), letting e.g. an <see cref="UserRole.Executive"/> or
/// <see cref="UserRole.Site"/> account overwrite a project's whole schedule. Interim decision
/// (orchestrator, pending a full po-analyst/domain-expert permission-matrix review): schedule
/// import (<c>xer</c>/<c>mspdi</c>) is restricted to PM/Planning/Admin; progress import
/// (<c>xlsx</c>) additionally allows Site/QS (the roles that actually record field progress -
/// progress is the input to EV -&gt; EVM -&gt; payment certification, ADR-0009). Enforced here
/// (Application/controller layer), not only in the frontend. The template-download endpoint
/// (<see cref="GetProgressTemplate"/>) is read-only and stays open to any authenticated user, per
/// the same decision. Because the two role sets differ *within the same dynamic <c>{format}</c>
/// route/action* (unlike <c>TenantApprovalPoliciesController</c>'s single fixed
/// <c>[Authorize(Roles = ...)]</c>), the gate is enforced imperatively against the resolved format
/// rather than declaratively - <c>[Authorize(Roles = ...)]</c> cannot itself vary per route-segment
/// value on one action.</para>
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/projects/{projectId:guid}/import")]
public sealed class ImportController(ISender sender, IImportOptionsProvider importOptions) : ControllerBase
{
    // Defense-in-depth ceiling above the configurable Import:MaxFileSizeBytes cap (default 50 MiB) -
    // deliberately higher, not equal to it: ASP.NET Core's [RequestSizeLimit] governs the whole
    // multipart request body (headers/boundaries/other form fields included), not just the file
    // part, so it must have headroom above the pure file-content cap it backstops. It is a static
    // ceiling (not derived from Import:MaxFileSizeBytes) - an operator who raises MaxFileSizeBytes
    // above 100 MB must also raise this constant, or uploads will fail at the framework layer with
    // an opaque 413 rather than the modelled FileTooLarge job (tracked as L-05, non-blocking).
    private const long MaxRequestBodyBytes = 100 * 1024 * 1024;

    private static readonly string[] ScheduleImportRoles =
        [nameof(UserRole.PM), nameof(UserRole.Planning), nameof(UserRole.Admin)];

    private static readonly string[] ProgressImportRoles =
        [nameof(UserRole.PM), nameof(UserRole.Planning), nameof(UserRole.Site), nameof(UserRole.QS), nameof(UserRole.Admin)];

    /// <summary><c>{format}</c> is one of <c>xer</c>, <c>mspdi</c>, <c>xlsx</c> (case-insensitive).
    /// Always returns a job id - a rejected file (too large, malformed, a relation cycle, an XXE
    /// attempt) is still a <see cref="FileImportJobDto"/> whose <c>status</c> is <c>Failed</c>, not
    /// an HTTP-level error (see <c>ImportScheduleFileCommandHandler</c>'s remarks).</summary>
    [HttpPost("{format}")]
    [RequestSizeLimit(MaxRequestBodyBytes)]
    public async Task<IActionResult> Import(
        Guid projectId, string format, IFormFile file, CancellationToken cancellationToken)
    {
        var normalizedFormat = format.Trim().ToLowerInvariant();

        // S3-SEC-01 M-04: checked before anything else (file presence, size, content) - an excluded
        // role is refused outright, never given a Failed job/diagnostic detail about the file itself.
        var allowedRoles = normalizedFormat switch
        {
            "xer" or "mspdi" => ScheduleImportRoles,
            "xlsx" => ProgressImportRoles,
            _ => null, // an unsupported format is rejected below regardless of role - no gate needed.
        };

        if (allowedRoles is not null && !allowedRoles.Any(User.IsInRole))
        {
            return ResultProblemMapper.ToActionResult(ImportErrorCodes.Forbidden, HttpContext.Request.Path);
        }

        if (file is null || file.Length == 0)
        {
            return ResultProblemMapper.ToActionResult(ImportErrorCodes.MalformedFile, HttpContext.Request.Path);
        }

        // S3-SEC-01 M-02: IFormFile.Length is the multipart part's declared Content-Length, known at
        // action-invocation time - before CopyToAsync ever reads a byte of the body. An upload already
        // known to exceed the configured cap is never buffered into a MemoryStream at all: only a
        // single placeholder byte is passed through as Content, while the real (oversized) length
        // travels separately as declaredContentLength - what the command handler's cap check actually
        // compares against MaxFileSizeBytes, so the FileTooLarge outcome is unchanged even though the
        // true bytes are never read into managed memory. A within-cap file is copied into a
        // pre-sized buffer (avoids MemoryStream's internal doubling-growth reallocation) as before.
        long declaredContentLength = file.Length;
        byte[] content;
        if (file.Length > importOptions.MaxFileSizeBytes)
        {
            content = [0];
        }
        else
        {
            await using var stream = new MemoryStream(checked((int)file.Length));
            await file.CopyToAsync(stream, cancellationToken);
            content = stream.ToArray();
        }

        var result = normalizedFormat switch
        {
            "xer" => await sender.Send(
                new ImportScheduleFileCommand(projectId, ImportFileFormat.Xer, file.FileName, content, declaredContentLength), cancellationToken),
            "mspdi" => await sender.Send(
                new ImportScheduleFileCommand(projectId, ImportFileFormat.Mspdi, file.FileName, content, declaredContentLength), cancellationToken),
            "xlsx" => await sender.Send(
                new ImportExcelProgressCommand(projectId, file.FileName, content, declaredContentLength), cancellationToken),
            _ => Domain.Common.Result<FileImportJobDto>.Failure(ImportErrorCodes.UnsupportedFormat),
        };

        if (result.IsFailure)
        {
            return ResultProblemMapper.ToActionResult(result.Error, HttpContext.Request.Path);
        }

        return CreatedAtAction(nameof(GetJob), new { projectId, jobId = result.Value.Id }, result.Value);
    }

    [HttpGet("jobs/{jobId:guid}")]
    public async Task<IActionResult> GetJob(Guid projectId, Guid jobId, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetImportJobQuery(projectId, jobId), cancellationToken);

        return result.IsSuccess
            ? Ok(result.Value)
            : ResultProblemMapper.ToActionResult(result.Error, HttpContext.Request.Path);
    }

    /// <summary>Newest-first, paged job history (S3-BE-04 DoD: "history, not fire-and-forget").</summary>
    [HttpGet("jobs")]
    public async Task<IActionResult> GetJobHistory(
        Guid projectId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var jobs = await sender.Send(new GetImportJobHistoryQuery(projectId, page, pageSize), cancellationToken);
        return Ok(jobs);
    }

    /// <summary>S3-BE-03 export half: a blank progress-update template listing every activity in the project.</summary>
    [HttpGet("xlsx/template")]
    public async Task<IActionResult> GetProgressTemplate(Guid projectId, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new ExportProgressTemplateQuery(projectId), cancellationToken);

        if (result.IsFailure)
        {
            return ResultProblemMapper.ToActionResult(result.Error, HttpContext.Request.Path);
        }

        return File(
            result.Value,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "progress-update-template.xlsx");
    }
}
