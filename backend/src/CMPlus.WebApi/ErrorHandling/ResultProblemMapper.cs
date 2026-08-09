using CMPlus.Application.Approval;
using CMPlus.Application.Common;
using CMPlus.Application.Features.ActualCosts;
using CMPlus.Application.Features.Approval;
using CMPlus.Application.Features.CashFlow;
using CMPlus.Application.Features.Cpm;
using CMPlus.Application.Features.Dashboard;
using CMPlus.Application.Features.Evm;
using CMPlus.Application.Features.Gantt;
using CMPlus.Application.Features.Payment;
using CMPlus.Application.Features.Projects;
using CMPlus.Application.Features.Progress;
using CMPlus.Application.Import;
using CMPlus.Application.Services.Cpm;
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

            // S5-BE-04: RecalculateCpmCommand. CpmValidationErrorCodes.CycleDetected is handled
            // separately below (its Result.Error carries a dynamic ": A -> B -> A" chain suffix, so
            // it cannot be an exact dictionary key) - DuplicateRelation/UnknownActivityInRelation
            // never carry a dynamic suffix and are exact-matched here like every other code.
            [CpmErrorCodes.ProjectNotFound] = (StatusCodes.Status404NotFound, "not-found", "The requested resource was not found."),
            [CpmValidationErrorCodes.DuplicateRelation] = (StatusCodes.Status422UnprocessableEntity, "cpm-duplicate-relation", "Two relations exist between the same pair of activities in the same direction."),
            [CpmValidationErrorCodes.UnknownActivityInRelation] = (StatusCodes.Status422UnprocessableEntity, "cpm-unknown-activity", "A relation references an activity outside this project's schedule."),

            // S6-BE-01: the Gantt read (GetGanttQuery).
            [GanttErrorCodes.ProjectNotFound] = (StatusCodes.Status404NotFound, "not-found", "The requested resource was not found."),

            // S7-BE-03/05: the EVM read and period-close commands.
            [EvmErrorCodes.ProjectNotFound] = (StatusCodes.Status404NotFound, "not-found", "The requested resource was not found."),
            [EvmErrorCodes.SnapshotAlreadyExists] = (StatusCodes.Status409Conflict, "evm-snapshot-already-exists", "An EVM period snapshot for this data date has already been closed."),
            [EvmErrorCodes.InvalidSnapshotRange] = (StatusCodes.Status400BadRequest, "evm-invalid-snapshot-range", "The 'from' date must not be later than the 'to' date."),

            // S8 (actual-cost.md §9/§12, ADR-0013): RecordActualCostCommand.
            [ActualCostErrorCodes.ProjectNotFound] = (StatusCodes.Status404NotFound, "not-found", "The requested resource was not found."),
            [ActualCostErrorCodes.WbsNodeNotFound] = (StatusCodes.Status400BadRequest, "actual-cost-wbs-node-not-found", "The WBS node does not belong to this project."),
            [ActualCostErrorCodes.ActivityNotFound] = (StatusCodes.Status400BadRequest, "actual-cost-activity-not-found", "The activity does not belong to this project."),
            [ActualCostErrorCodes.ReversedEntryNotFound] = (StatusCodes.Status400BadRequest, "actual-cost-reversed-entry-not-found", "ReversesEntryId does not reference an existing cost entry in this project."),
            [ActualCostErrorCodes.NoteRequiredForClosedPeriod] = (StatusCodes.Status422UnprocessableEntity, "actual-cost-note-required-for-closed-period", "A note is required when posting a cost entry into an already-closed EVM period."),

            // S8-BE-01: GetCashFlowQuery.
            [CashFlowErrorCodes.ProjectNotFound] = (StatusCodes.Status404NotFound, "not-found", "The requested resource was not found."),
            [CashFlowErrorCodes.InvalidRange] = (StatusCodes.Status400BadRequest, "cash-flow-invalid-range", "The 'from' date must not be later than the effective data date."),

            // S8-BE-02: GetDashboardQuery.
            [DashboardErrorCodes.ProjectNotFound] = (StatusCodes.Status404NotFound, "not-found", "The requested resource was not found."),

            // S9-BE-05: PaymentCertificate approval-chain commands (Submit/Approve/ReturnForRevision/
            // Reject/RecordPayment). ApprovalErrorCodes.PolicyGap (422 approval-policy-gap) and
            // "ApprovalPolicyNotFound" (404) above are reused as-is, not duplicated here.
            [PaymentApprovalErrorCodes.NotFound] = (StatusCodes.Status404NotFound, "not-found", "The requested resource was not found."),
            [PaymentApprovalErrorCodes.InvalidStatusForTransition] = (StatusCodes.Status409Conflict, "document-immutable", "The document is not in a state that allows this action."),
            [PaymentApprovalErrorCodes.NotAuthorizedForApprovalStep] = (StatusCodes.Status403Forbidden, "not-current-step", "You are not authorized to act on this document's current approval step."),
            [PaymentApprovalErrorCodes.SelfApprovalNotPermitted] = (StatusCodes.Status403Forbidden, "self-approval-not-permitted", "The document's creator or submitter may not approve their own submission."),
            [PaymentApprovalErrorCodes.DuplicateChainApprover] = (StatusCodes.Status403Forbidden, "duplicate-chain-approver", "You have already approved a different step of this document's current approval chain."),
            [PaymentApprovalErrorCodes.ConcurrencyConflict] = (StatusCodes.Status409Conflict, "concurrent-transition", "Another action has already changed this document. Reload and try again."),

            // Security review sprint-09.md M-03: a corrupt/legacy chain snapshot degrades to a clear
            // 409 instead of an unhandled 500.
            [PaymentApprovalErrorCodes.CorruptApprovalChain] = (StatusCodes.Status409Conflict, "corrupt-approval-chain", "This document's approval chain could not be resolved. Contact support."),
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

        // S5-BE-04: GraphValidator (via CpmEngine) reports a cycle as
        // "CpmCycleDetected: A -> B -> A" - a dynamic suffix, so it is matched by prefix here
        // (same reasoning as the ValidationErrorCodes branch above) rather than as an exact
        // dictionary key. The full detail (including the offending chain) is preserved for the
        // caller/frontend to display, same discipline as the validation-error branch.
        if (errorCode.StartsWith(CpmValidationErrorCodes.CycleDetected, StringComparison.Ordinal))
        {
            return new ProblemDetails
            {
                Status = StatusCodes.Status422UnprocessableEntity,
                Type = "https://cmplus.dev/problems/cpm-cycle-detected",
                Title = "The activity relation graph contains a cycle and cannot be scheduled.",
                Detail = errorCode,
                Instance = instancePath,
            };
        }

        // S7-BE-03: GetEvmQuery. An unrecognised ?eacVariant= value carries the raw offending string
        // as a dynamic suffix (same reasoning as the CpmValidationErrorCodes.CycleDetected branch
        // above) so the response Detail is actionable, never a silent fallback to the project default
        // (design.md §2.1).
        if (errorCode.StartsWith(EvmErrorCodes.InvalidEacVariantPrefix, StringComparison.Ordinal))
        {
            return new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Type = "https://cmplus.dev/problems/invalid-eac-variant",
                Title = "The requested EAC variant is not recognised.",
                Detail = errorCode,
                Instance = instancePath,
            };
        }

        // S9-BE-06: UpdateApprovalPolicyCommand. Both prefixes carry the offending StepNo as a
        // dynamic suffix (same reasoning as the two branches above) - surfaced as a proper
        // machine-readable ProblemDetails.Extensions member (design.md §2.2: "400 body carries
        // { invalidStepNo, problem }"), not only baked into Detail's free text.
        if (errorCode.StartsWith(ApprovalPolicyErrorCodes.BandOverlapPrefix, StringComparison.Ordinal)
            || errorCode.StartsWith(ApprovalPolicyErrorCodes.BandGapPrefix, StringComparison.Ordinal))
        {
            var isOverlap = errorCode.StartsWith(ApprovalPolicyErrorCodes.BandOverlapPrefix, StringComparison.Ordinal);
            var prefix = isOverlap ? ApprovalPolicyErrorCodes.BandOverlapPrefix : ApprovalPolicyErrorCodes.BandGapPrefix;
            var problemKind = isOverlap ? "BandOverlap" : "BandGap";

            var bandProblem = new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Type = isOverlap
                    ? "https://cmplus.dev/problems/approval-policy-band-overlap"
                    : "https://cmplus.dev/problems/approval-policy-band-gap",
                Title = "The approval policy's rule bands are invalid.",
                Detail = errorCode,
                Instance = instancePath,
            };

            bandProblem.Extensions["problem"] = problemKind;
            if (int.TryParse(errorCode[prefix.Length..], out var invalidStepNo) && invalidStepNo > 0)
            {
                bandProblem.Extensions["invalidStepNo"] = invalidStepNo;
            }

            return bandProblem;
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
