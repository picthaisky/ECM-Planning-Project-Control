using CMPlus.Application.Abstractions;
using CMPlus.Application.Features.Photos;
using CMPlus.Application.Features.Photos.Commands.UploadPhoto;
using CMPlus.Application.Features.Photos.Queries.GetPhotoContent;
using CMPlus.Domain.Enums;
using CMPlus.WebApi.ErrorHandling;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CMPlus.WebApi.Controllers.Photos;

/// <summary>
/// S12-BE-01 (US-12.1): <c>POST /api/v1/projects/{projectId}/photos</c> (multipart upload) and
/// <c>GET /api/v1/projects/{projectId}/photos/{photoId}</c> (serve). Thin per
/// clean-architecture-dotnet - every business decision (magic-byte check, size cap, EXIF stripping,
/// tenant/project ownership) lives in Application/Domain/Infrastructure; this controller only
/// parses the multipart request, dispatches to MediatR, and maps the result.
///
/// <para><b>Role-gating decision.</b> <see cref="WriteRoles"/> reuses
/// <c>ProjectWeatherLogsController.WriteRoles</c>/<c>ImportController.ProgressImportRoles</c>'s
/// established "who records field data" role set exactly (PM/Planning/Site/QS/Admin) - this task's
/// brief is explicit that Site users take photos in practice, and this is now the third site-data-
/// capture write path in this codebase to land on that same set, which is itself evidence it is the
/// right default rather than a per-feature guess. Reads are gated only by
/// <see cref="AuthorizeAttribute"/> with no role restriction, mirroring
/// <c>ProjectWeatherLogsController</c>'s identical "read wider than write" reasoning - a photo is
/// project documentation, not commercially sensitive the way cost/payment data is.</para>
///
/// <para><b>S12-SEC-01 hardening on <see cref="Get"/>.</b> <c>X-Content-Type-Options: nosniff</c> is
/// set explicitly on this response (this API sets no global security-header middleware yet - a
/// broader adoption is a separate follow-up, not silently expanded here) so a browser never content-
/// sniffs a served photo as anything other than the server-computed <c>image/jpeg</c>/<c>image/png</c>
/// (<c>Photo.ContentType</c> - never a stored free-text value). <c>fileDownloadName</c> forces
/// <c>Content-Disposition: attachment</c>, so even a byte sequence a browser might otherwise be
/// tempted to render inline is downloaded instead - the concrete defence against "an uploaded file
/// that renders as HTML in the user's origin is stored XSS" this task's brief called out by name.</para>
/// </summary>
[ApiController]
[Route("api/v1/projects/{projectId:guid}/photos")]
public sealed class ProjectPhotosController(ISender sender, IPhotoOptionsProvider photoOptions) : ControllerBase
{
    // Defense-in-depth ceiling above the configurable Photos:MaxFileSizeBytes cap (default 10 MiB) -
    // deliberately higher, not equal to it, for the same reason ImportController's own
    // MaxRequestBodyBytes is: [RequestSizeLimit] governs the whole multipart request body
    // (headers/boundaries/other form fields included), not just the file part.
    private const long MaxRequestBodyBytes = 20 * 1024 * 1024;

    internal const string WriteRoles =
        $"{nameof(UserRole.PM)},{nameof(UserRole.Planning)},{nameof(UserRole.Site)},{nameof(UserRole.QS)},{nameof(UserRole.Admin)}";

    [HttpPost]
    [Authorize(Roles = WriteRoles)]
    [RequestSizeLimit(MaxRequestBodyBytes)]
    public async Task<IActionResult> Upload(
        Guid projectId,
        IFormFile file,
        [FromForm] Guid? activityId,
        [FromForm] string? caption,
        [FromForm] DateTimeOffset? capturedAt,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return ResultProblemMapper.ToActionResult(PhotoErrorCodes.FileRequired, HttpContext.Request.Path);
        }

        // S12-BE-01 DoD: "enforce it before buffering the whole thing into memory" - IFormFile.Length
        // is the multipart part's declared Content-Length, known before CopyToAsync ever reads a
        // byte of the body. Unlike ImportController.Import (which still buffers a placeholder and
        // hands the oversize decision to its handler, because a rejected import must still produce a
        // queryable Failed job), a rejected photo upload has no job-history concept to populate - so
        // this rejects outright here, and neither CopyToAsync NOR sender.Send is ever reached for an
        // oversized upload (proven directly, not merely by code inspection, in
        // ProjectPhotosControllerUploadBufferingTests).
        if (file.Length > photoOptions.MaxFileSizeBytes)
        {
            return ResultProblemMapper.ToActionResult(PhotoErrorCodes.FileTooLarge, HttpContext.Request.Path);
        }

        byte[] content;
        await using (var stream = new MemoryStream(checked((int)file.Length)))
        {
            await file.CopyToAsync(stream, cancellationToken);
            content = stream.ToArray();
        }

        var command = new UploadPhotoCommand(projectId, activityId, caption, capturedAt, content, file.Length);
        var result = await sender.Send(command, cancellationToken);

        if (result.IsFailure)
        {
            return ResultProblemMapper.ToActionResult(result.Error, HttpContext.Request.Path);
        }

        return CreatedAtAction(nameof(Get), new { projectId, photoId = result.Value.Id }, result.Value);
    }

    [HttpGet("{photoId:guid}")]
    [Authorize]
    public async Task<IActionResult> Get(Guid projectId, Guid photoId, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetPhotoContentQuery(projectId, photoId), cancellationToken);

        if (result.IsFailure)
        {
            return ResultProblemMapper.ToActionResult(result.Error, HttpContext.Request.Path);
        }

        // S12-SEC-01: no dangerous content sniffing - see this controller's class remarks.
        //
        // Security review sprint-12.md L-05: indexer assignment, not Headers.Append - Append would
        // add a SECOND "X-Content-Type-Options" value ("nosniff,nosniff") the moment a global
        // security-header middleware also sets this header, which per the Fetch spec browsers still
        // read correctly today (they take values[0]) but is a smell that would misbehave the day
        // that middleware lands.
        Response.Headers["X-Content-Type-Options"] = "nosniff";

        return File(result.Value.Content, result.Value.ContentType, result.Value.FileName);
    }
}
