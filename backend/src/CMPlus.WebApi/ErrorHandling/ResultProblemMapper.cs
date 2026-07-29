using CMPlus.Application.Approval;
using CMPlus.Application.Common;
using CMPlus.Application.Features.Projects;
using CMPlus.Application.Features.Progress;
using CMPlus.Application.Import;
using CMPlus.Application.Wbs;
using Microsoft.AspNetCore.Mvc;

namespace CMPlus.WebApi.ErrorHandling;

/// <summary>
/// Maps a failed <see cref="CMPlus.Domain.Common.Result"/>'s error code (S2-BE-03) to the stable
/// RFC 7807 <c>type</c>/<c>status</c>/<c>title</c> triple documented in design.md §2.3 ("Error
/// `type` values that the frontend must handle explicitly"). Codes not in the table still produce
/// a well-formed <see cref="ProblemDetails"/> (400, generic type) rather than an unmapped 500 -
/// only a genuinely unexpected exception reaches <see cref="GlobalExceptionHandler"/>.
///
/// S4-BE-06 (gap closure): a <see cref="CMPlus.Application.Common.ValidationBehavior{TRequest,TResponse}"/>
/// failure is recognized by its <see cref="ValidationErrorCodes.Prefix"/> marker *before* the
/// table lookup below - every such failure maps to the same <c>validation-error</c> type/title
/// <see cref="GlobalExceptionHandler"/> already uses for its own (rarely-hit) non-Result-shaped
/// <c>ValidationException</c> fallback, so the frontend sees one consistent "this is a field-
/// validation problem" signal regardless of which of the two code paths produced it - never the
/// generic <c>bad-request</c> bucket a raw FluentValidation message previously fell into.
/// </summary>
public static class ResultProblemMapper
{
    private static readonly IReadOnlyDictionary<string, (int Status, string Type, string Title)> KnownErrors =
        new Dictionary<string, (int, string, string)>
        {
            ["InvalidCredentials"] = (StatusCodes.Status401Unauthorized, "invalid-credentials", "The email or password is incorrect."),
            [ApprovalErrorCodes.PolicyGap] = (StatusCodes.Status422UnprocessableEntity, "approval-policy-gap", "No approval chain could be resolved for this amount; submission is blocked."),
            ["ApprovalPolicyNotFound"] = (StatusCodes.Status404NotFound, "not-found", "The requested resource was not found."),

            // S3-BE-01..04: import pipeline. Note that a rejected/failed *file* (size cap, cycle,
            // XXE, malformed content) is deliberately NOT one of these - ImportScheduleFileCommand/
            // ImportExcelProgressCommand return Result.Success with a Failed FileImportJobDto for
            // those (see that handler's remarks); only a request that never produced a job at all
            // reaches this table.
            [ImportErrorCodes.ProjectNotFound] = (StatusCodes.Status404NotFound, "not-found", "The requested resource was not found."),
            [ImportErrorCodes.JobNotFound] = (StatusCodes.Status404NotFound, "not-found", "The requested resource was not found."),
            [ImportErrorCodes.UnsupportedFormat] = (StatusCodes.Status400BadRequest, "import-unsupported-format", "The import format must be xer, mspdi, or xlsx."),

            // S3-SEC-01 M-04: the caller is authenticated but their role is not permitted to perform
            // this specific kind of import - a genuine request-level rejection (never a Failed job),
            // since the request never reaches the point of creating one.
            [ImportErrorCodes.Forbidden] = (StatusCodes.Status403Forbidden, "import-forbidden", "Your role is not permitted to perform this import."),

            // S4-BE-01..03: WBS tree read, Project master-data edit, batch progress write.
            [WbsErrorCodes.ProjectNotFound] = (StatusCodes.Status404NotFound, "not-found", "The requested resource was not found."),
            [ProjectErrorCodes.NotFound] = (StatusCodes.Status404NotFound, "not-found", "The requested resource was not found."),
            [ProgressErrorCodes.ProjectNotFound] = (StatusCodes.Status404NotFound, "not-found", "The requested resource was not found."),
            [ProgressErrorCodes.UnknownActivity] = (StatusCodes.Status400BadRequest, "progress-unknown-activity", "One or more ActivityId values do not belong to this project."),

            // S4-BE-05: activities-under-a-node read (GetNodeActivitiesQuery).
            [WbsErrorCodes.NodeNotFound] = (StatusCodes.Status404NotFound, "not-found", "The requested resource was not found."),
        };

    public static ProblemDetails ToProblemDetails(string errorCode, string instancePath)
    {
        // S4-BE-06 (gap closure): checked ahead of the table lookup, since the *value* here is the
        // (variable, per-request) joined FluentValidation message text, not a stable dictionary
        // key - only the fixed prefix is stable and greppable.
        if (errorCode.StartsWith(ValidationErrorCodes.Prefix, StringComparison.Ordinal))
        {
            return new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Type = "https://cmplus.dev/problems/validation-error",
                Title = "One or more validation errors occurred.",
                // Still the raw, English, validator-authored message(s) - unchanged content from
                // before this fix. Never shown to a user directly (every frontend error-translation
                // module here classifies purely off `type`/`detail`-as-a-known-code and falls back
                // to a generic localized message otherwise - see e.g. `features/info/api.ts`'s
                // `toProjectApiError`) but kept for logs/diagnostics.
                Detail = errorCode[ValidationErrorCodes.Prefix.Length..],
                Instance = instancePath,
            };
        }

        var (status, type, title) = KnownErrors.TryGetValue(errorCode, out var mapping)
            ? mapping
            : (StatusCodes.Status400BadRequest, "bad-request", "The request could not be completed.");

        return new ProblemDetails
        {
            Status = status,
            Type = $"https://cmplus.dev/problems/{type}",
            Title = title,
            Detail = errorCode,
            Instance = instancePath,
        };
    }

    public static IActionResult ToActionResult(string errorCode, string instancePath)
    {
        var problem = ToProblemDetails(errorCode, instancePath);
        return new ObjectResult(problem)
        {
            StatusCode = problem.Status,
            ContentTypes = { "application/problem+json" },
        };
    }
}
