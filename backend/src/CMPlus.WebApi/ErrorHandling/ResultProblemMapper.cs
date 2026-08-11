using CMPlus.Application.Approval;
using CMPlus.Application.Common;
using CMPlus.Application.Features.ActualCosts;
using CMPlus.Application.Features.Baseline;
using CMPlus.Application.Features.Approval;
using CMPlus.Application.Features.CashFlow;
using CMPlus.Application.Features.Cpm;
using CMPlus.Application.Features.Dashboard;
using CMPlus.Application.Features.Eot;
using CMPlus.Application.Features.Evm;
using CMPlus.Application.Features.Gantt;
using CMPlus.Application.Features.Issues;
using CMPlus.Application.Features.Manpower;
using CMPlus.Application.Features.Payment;
using CMPlus.Application.Features.Photos;
using CMPlus.Application.Features.Projects;
using CMPlus.Application.Features.Progress;
using CMPlus.Application.Features.VariationOrder;
using CMPlus.Application.Features.Weather;
using CMPlus.Application.Idempotency;
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
            // S10-BE-02 (domain-rules.md §4.6): a cumulative-VO-escalation threshold is configured but
            // the project's baseline contract value is missing/<=0 - fail closed, never silently skip
            // the escalation test (the exact defect the shipped ApprovalRoutingService.cs:66 had).
            [ApprovalErrorCodes.ContractValueNotConfigured] = (StatusCodes.Status422UnprocessableEntity, "contract-value-not-configured", "The project's baseline contract value is not configured; cumulative-VO escalation cannot be evaluated."),
            ["ApprovalPolicyNotFound"] = (StatusCodes.Status404NotFound, "not-found", "The requested resource was not found."),
            // Same-shape concurrent-request race as BaselineErrorCodes.ConcurrencyConflict (see that
            // entry's remarks) - found during the S14-BE-01 Baseline fix's own review and fixed here
            // for ApprovalPolicy too (ApprovalPolicyConfiguration ships the identical filtered-unique-
            // index shape).
            [ApprovalPolicyErrorCodes.ConcurrencyConflict] = (StatusCodes.Status409Conflict, "concurrent-transition", "Another action has already changed the active approval policy for this document type. Reload and try again."),

            // S15-BE-01: SimulateApprovalRoutingQuery. ApprovalErrorCodes.PolicyGap/
            // ContractValueNotConfigured above are reused as-is by the simulator, not duplicated here.
            [ApprovalSimulationErrorCodes.ProjectNotFound] = (StatusCodes.Status404NotFound, "not-found", "The requested resource was not found."),

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
            // ADR-0019: RecalculateCpmCommand now also captures an append-only CpmRun, whose
            // TriggeredByUserId must never be fabricated.
            [CpmErrorCodes.ActorRequired] = (StatusCodes.Status401Unauthorized, "cpm-actor-required", "The current request has no resolvable authenticated user."),
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
            // ADR-0016: renamed from DuplicateChainApprover/"duplicate-chain-approver" - widened from
            // Approve-only to Approve-or-Reject, so the wording can no longer say "approved".
            [PaymentApprovalErrorCodes.DuplicateChainVoter] = (StatusCodes.Status403Forbidden, "duplicate-chain-voter", "You have already voted (approved or rejected) on a different step of this document's current approval chain."),
            [PaymentApprovalErrorCodes.ConcurrencyConflict] = (StatusCodes.Status409Conflict, "concurrent-transition", "Another action has already changed this document. Reload and try again."),

            // Security review sprint-09.md M-03: a corrupt/legacy chain snapshot degrades to a clear
            // 409 instead of an unhandled 500.
            [PaymentApprovalErrorCodes.CorruptApprovalChain] = (StatusCodes.Status409Conflict, "corrupt-approval-chain", "This document's approval chain could not be resolved. Contact support."),

            // S10-BE-01/02/03: VariationOrder approval-chain commands (Create/UpdateContent/Submit/
            // Approve/ReturnForRevision/Reject/Withdraw/Cancel). Several codes intentionally reuse the
            // exact type/title strings PaymentApprovalErrorCodes above already uses for the same
            // meaning (VariationOrderErrorCodes' own doc comments call this out explicitly) so the
            // frontend's error-translation layer does not need a second, parallel set of type strings
            // for what is semantically the identical failure on a second document type.
            [VariationOrderErrorCodes.NotFound] = (StatusCodes.Status404NotFound, "not-found", "The requested resource was not found."),
            [VariationOrderErrorCodes.ProjectNotFound] = (StatusCodes.Status404NotFound, "not-found", "The requested resource was not found."),
            [VariationOrderErrorCodes.ActorRequired] = (StatusCodes.Status401Unauthorized, "variation-order-actor-required", "The current request has no resolvable authenticated user."),
            [PaymentApprovalErrorCodes.ActorRequired] = (StatusCodes.Status401Unauthorized, "actor-required", "The current request has no resolvable authenticated user."),
            [VariationOrderErrorCodes.DuplicateVoNumber] = (StatusCodes.Status409Conflict, "duplicate-vo-number", "A variation order with this VoNumber already exists for this project."),
            [VariationOrderErrorCodes.UnknownActivity] = (StatusCodes.Status400BadRequest, "variation-order-unknown-activity", "One or more ActivityId values do not belong to this project."),
            [VariationOrderErrorCodes.ScopeBudgetMismatch] = (StatusCodes.Status422UnprocessableEntity, "vo-scope-budget-mismatch", "The scope payload's net budget delta does not equal the variation order's Amount exactly."),
            [VariationOrderErrorCodes.EmptyVariation] = (StatusCodes.Status422UnprocessableEntity, "empty-variation", "A variation order with zero Amount, zero TimeImpactDays and an empty scope payload carries no effect."),
            [VariationOrderErrorCodes.InvalidStatusForTransition] = (StatusCodes.Status409Conflict, "document-immutable", "The document is not in a state that allows this action."),
            [VariationOrderErrorCodes.NotAuthorizedForApprovalStep] = (StatusCodes.Status403Forbidden, "not-current-step", "You are not authorized to act on this document's current approval step."),
            [VariationOrderErrorCodes.SelfApprovalNotPermitted] = (StatusCodes.Status403Forbidden, "self-approval-not-permitted", "The document's creator or submitter may not approve their own submission."),
            [VariationOrderErrorCodes.DuplicateChainVoter] = (StatusCodes.Status403Forbidden, "duplicate-chain-voter", "You have already voted (approved or rejected) on a different step of this document's current approval chain."),
            [VariationOrderErrorCodes.ConcurrencyConflict] = (StatusCodes.Status409Conflict, "concurrent-transition", "Another action has already changed this document. Reload and try again."),
            [ProjectErrorCodes.ConcurrencyConflict] = (StatusCodes.Status409Conflict, "concurrent-transition", "Another action has already changed this project. Reload and try again."),
            [ProjectErrorCodes.BacLockedByApprovedVariationOrders] = (StatusCodes.Status409Conflict, "bac-locked-by-variation-orders", "Budget at completion is derived from the original budget plus approved Variation Orders and cannot be edited directly. Raise a Variation Order instead."),
            // S14-BE-03: SetEacVariantDefaultCommand/SetEacAdvancedInputsCommand's shared guard,
            // approached from either direction (selecting the variant vs clearing its input).
            [ProjectErrorCodes.EacManualEtcRequiredForBottomUpEtc] = (StatusCodes.Status400BadRequest, "eac-manual-etc-required-for-bottom-up-etc", "EacManualEtc must be configured before EacVariantDefault can be BottomUpEtc."),
            [ProjectErrorCodes.EacCustomPerformanceFactorRequiredForCustomPf] = (StatusCodes.Status400BadRequest, "eac-custom-performance-factor-required-for-custom-pf", "EacCustomPerformanceFactor must be configured before EacVariantDefault can be CustomPf."),
            [VariationOrderErrorCodes.CorruptApprovalChain] = (StatusCodes.Status409Conflict, "corrupt-approval-chain", "This document's approval chain could not be resolved. Contact support."),
            [VariationOrderErrorCodes.WithdrawAfterVoteCast] = (StatusCodes.Status409Conflict, "document-immutable", "This document already has at least one vote recorded this revision and can no longer be withdrawn."),
            // domain-rules.md §5.1's non-negativity guard.
            [VariationOrderErrorCodes.WouldMakeContractValueNegative] = (StatusCodes.Status422UnprocessableEntity, "vo-would-make-contract-value-negative", "Approving this variation order would drive BAC or ContractValue below zero."),
            // domain-rules.md §5.2's "omission of already-executed work" rule.
            [VariationOrderErrorCodes.OmitsExecutedScope] = (StatusCodes.Status422UnprocessableEntity, "vo-omits-executed-scope", "A negative budget delta targets an activity that already has recorded progress."),
            [VariationOrderErrorCodes.ScopeActivityBudgetWouldBeNegative] = (StatusCodes.Status422UnprocessableEntity, "vo-scope-activity-budget-would-be-negative", "A scope line's delta would drive that activity's own budget below zero."),
            // domain-rules.md §4.7: the escalation-bypass guard, re-checked on the final approving step.
            [VariationOrderErrorCodes.EscalationThresholdCrossedSinceSubmission] = (StatusCodes.Status409Conflict, "vo-escalation-threshold-crossed-since-submission", "The cumulative-VO escalation threshold has been crossed since this document was submitted; return it for revision and resubmit to pick up the required signature."),

            // S11-BE-01: RecordWeatherLogCommand/RecordWeatherLogCorrectionCommand/ListWeatherLogsQuery
            // (domain-rules.md weather-eot §8).
            [WeatherLogErrorCodes.ProjectNotFound] = (StatusCodes.Status404NotFound, "not-found", "The requested resource was not found."),
            [WeatherLogErrorCodes.ActorRequired] = (StatusCodes.Status401Unauthorized, "weather-log-actor-required", "The current request has no resolvable authenticated user."),
            [WeatherLogErrorCodes.UnknownActivity] = (StatusCodes.Status400BadRequest, "weather-log-unknown-activity", "One or more AffectedActivityIds values do not belong to this project."),
            [WeatherLogErrorCodes.InvalidDateRange] = (StatusCodes.Status400BadRequest, "weather-log-invalid-date-range", "The 'from' date must not be later than the 'to' date."),
            // §8.2 chain-integrity rules 1/2/4.
            [WeatherLogErrorCodes.CorrectionTargetNotFound] = (StatusCodes.Status422UnprocessableEntity, "weather-log-correction-target-not-found", "The weather log entry being corrected does not exist in this project."),
            [WeatherLogErrorCodes.AlreadySuperseded] = (StatusCodes.Status409Conflict, "weather-log-already-superseded", "This weather log entry already has a correction; target the current chain tail instead."),
            [WeatherLogErrorCodes.CorrectionOrdering] = (StatusCodes.Status422UnprocessableEntity, "weather-log-correction-ordering", "A correction must be recorded after the entry it corrects."),
            // §8.3: the deliberate 405 for the routes that do not exist for this aggregate.
            [WeatherLogErrorCodes.IsImmutable] = (StatusCodes.Status405MethodNotAllowed, "weather-log-is-immutable", "Weather log entries cannot be updated or deleted. Submit a correction instead."),

            // S11-BE-03: CreateIssueCommand/AdvanceIssueStatusCommand/ListIssuesQuery
            // (domain-rules.md weather-eot §9).
            [IssueLogErrorCodes.ProjectNotFound] = (StatusCodes.Status404NotFound, "not-found", "The requested resource was not found."),
            [IssueLogErrorCodes.ActorRequired] = (StatusCodes.Status401Unauthorized, "issue-log-actor-required", "The current request has no resolvable authenticated user."),
            [IssueLogErrorCodes.NotFound] = (StatusCodes.Status404NotFound, "not-found", "The requested resource was not found."),
            [IssueLogErrorCodes.AlreadyClosed] = (StatusCodes.Status409Conflict, "issue-already-closed", "This issue is already Closed and cannot be advanced further."),
            [IssueLogErrorCodes.ConcurrencyConflict] = (StatusCodes.Status409Conflict, "concurrent-transition", "Another action has already changed this issue. Reload and try again."),

            // S11-BE-02: EvaluateEotCommand (domain-rules.md weather-eot, the EotEvaluator).
            [EotErrorCodes.ProjectNotFound] = (StatusCodes.Status404NotFound, "not-found", "The requested resource was not found."),
            [EotErrorCodes.ActorRequired] = (StatusCodes.Status401Unauthorized, "eot-actor-required", "The current request has no resolvable authenticated user."),
            // §3.3: "No default calendar ⟹ block, never guess" - same fail-closed discipline as ApprovalPolicyGap.
            [EotErrorCodes.ProjectCalendarNotConfigured] = (StatusCodes.Status422UnprocessableEntity, "eot-project-calendar-not-configured", "The project has no default working-day calendar configured; an EOT evaluation cannot be computed."),
            // §4.4: the project has never had a single CpmRun captured - the contemporaneous criticality ruling is unanswerable even in degraded form.
            [EotErrorCodes.NoCpmRunAvailable] = (StatusCodes.Status422UnprocessableEntity, "eot-no-cpm-run-available", "The project has no CPM schedule calculation on record; an EOT evaluation cannot be computed."),
            [EotErrorCodes.InvalidWindowRange] = (StatusCodes.Status400BadRequest, "eot-invalid-window-range", "The evaluation window's end date must not be earlier than its start date."),
            // §5.1: a governing CpmRun's stored ProjectDurationDays disagrees with re-running CpmEngine
            // against its own captured network - a data-integrity problem, never a user input error.
            [EotErrorCodes.CpmRunDataCorrupt] = (StatusCodes.Status500InternalServerError, "eot-cpm-run-data-corrupt", "A governing CPM run's stored data is internally inconsistent. Contact support."),

            // S12-BE-01 (US-12.1): UploadPhotoCommand/GetPhotoContentQuery.
            [PhotoErrorCodes.ProjectNotFound] = (StatusCodes.Status404NotFound, "not-found", "The requested resource was not found."),
            [PhotoErrorCodes.ActorRequired] = (StatusCodes.Status401Unauthorized, "photo-actor-required", "The current request has no resolvable authenticated user."),
            [PhotoErrorCodes.UnknownActivity] = (StatusCodes.Status400BadRequest, "photo-unknown-activity", "ActivityId does not belong to this project."),
            [PhotoErrorCodes.FileRequired] = (StatusCodes.Status400BadRequest, "photo-file-required", "A file must be uploaded."),
            [PhotoErrorCodes.FileTooLarge] = (StatusCodes.Status400BadRequest, "photo-file-too-large", "The uploaded file exceeds the configured size limit."),
            // S12-SEC-01: magic bytes did not match a supported image format - never the client-
            // supplied filename/Content-Type, both attacker-controlled.
            [PhotoErrorCodes.UnsupportedFormat] = (StatusCodes.Status400BadRequest, "photo-unsupported-format", "Only JPEG and PNG images are supported."),
            [PhotoErrorCodes.MalformedImage] = (StatusCodes.Status400BadRequest, "photo-malformed-image", "The uploaded file's internal structure could not be safely processed."),
            // Deliberately the generic not-found type/title - a cross-tenant id, a wrong-project id,
            // and an unknown id must be indistinguishable (ADR-0002; this task's brief).
            [PhotoErrorCodes.NotFound] = (StatusCodes.Status404NotFound, "not-found", "The requested resource was not found."),

            // S12-BE-02 (US-12.2): RecordManpowerLogCommand/RecordManpowerLogCorrectionCommand/
            // GetProductivityIndexQuery (domain-rules.md (manpower-equipment) §4/§5).
            [ManpowerLogErrorCodes.ProjectNotFound] = (StatusCodes.Status404NotFound, "not-found", "The requested resource was not found."),
            [ManpowerLogErrorCodes.ActorRequired] = (StatusCodes.Status401Unauthorized, "manpower-log-actor-required", "The current request has no resolvable authenticated user."),
            [ManpowerLogErrorCodes.WorkCategoryNotInProject] = (StatusCodes.Status422UnprocessableEntity, "manpower-log-work-category-not-in-project", "WorkCategoryId does not belong to this project."),
            [ManpowerLogErrorCodes.WbsNodeNotInProject] = (StatusCodes.Status422UnprocessableEntity, "manpower-log-wbs-node-not-in-project", "WbsNodeId belongs to a different project in this tenant."),
            // ADR-0002/fixture M-14b: cross-tenant and unknown are indistinguishable - always 404, never 422.
            [ManpowerLogErrorCodes.WbsNodeNotFound] = (StatusCodes.Status404NotFound, "not-found", "The requested resource was not found."),
            [ManpowerLogErrorCodes.ActivityNotInProject] = (StatusCodes.Status422UnprocessableEntity, "manpower-log-activity-not-in-project", "ActivityId belongs to a different project in this tenant."),
            [ManpowerLogErrorCodes.ActivityNotFound] = (StatusCodes.Status404NotFound, "not-found", "The requested resource was not found."),
            [ManpowerLogErrorCodes.ActivityWbsNodeMismatch] = (StatusCodes.Status422UnprocessableEntity, "manpower-log-activity-wbs-node-mismatch", "ActivityId's own WbsNodeId disagrees with the row's WbsNodeId."),
            [ManpowerLogErrorCodes.AlreadyExists] = (StatusCodes.Status409Conflict, "manpower-log-already-exists", "An in-force entry already exists for this exact day/shift/category/node/labour-type/subcontractor. Retry with allowDuplicate=true to confirm a genuine second crew."),
            // §4.7 chain-integrity rules 1/2/4.
            [ManpowerLogErrorCodes.CorrectionTargetNotFound] = (StatusCodes.Status422UnprocessableEntity, "manpower-log-correction-target-not-found", "The manpower log entry being corrected does not exist in this project."),
            [ManpowerLogErrorCodes.AlreadySuperseded] = (StatusCodes.Status409Conflict, "manpower-log-already-superseded", "This manpower log entry already has a correction; target the current chain tail instead."),
            [ManpowerLogErrorCodes.CorrectionOrdering] = (StatusCodes.Status422UnprocessableEntity, "manpower-log-correction-ordering", "A correction must be recorded after the entry it corrects."),
            // §4.7: the deliberate 405 for the routes that do not exist for this aggregate.
            [ManpowerLogErrorCodes.IsImmutable] = (StatusCodes.Status405MethodNotAllowed, "manpower-log-is-immutable", "Manpower log entries cannot be updated or deleted. Submit a correction instead."),
            [ManpowerLogErrorCodes.InvalidDateRange] = (StatusCodes.Status400BadRequest, "manpower-log-invalid-date-range", "The 'from' date must not be later than the 'to' date."),

            // S13-BE-01 (US-13.1, ADR-0005): IdempotencyMiddleware. Not a MediatR handler, so these
            // never flow through a Result - the middleware calls ResultProblemMapper.ToProblemDetails
            // directly so every error this API returns still shares one ProblemDetails shape/table.
            [IdempotencyErrorCodes.PayloadMismatch] = (StatusCodes.Status409Conflict, "idempotency-payload-mismatch", "This Idempotency-Key was already used with a different request. Use a new key for a different operation."),
            [IdempotencyErrorCodes.RequestInProgress] = (StatusCodes.Status409Conflict, "idempotency-request-in-progress", "A request with this Idempotency-Key is already being processed. Retry shortly."),
            [IdempotencyErrorCodes.ActorRequired] = (StatusCodes.Status401Unauthorized, "idempotency-actor-required", "The current request has no resolvable authenticated user."),
            [IdempotencyErrorCodes.KeyInvalid] = (StatusCodes.Status400BadRequest, "idempotency-key-invalid", "The Idempotency-Key header must be a single, non-empty value no longer than 200 characters."),
            [IdempotencyErrorCodes.ResponseNotReplayable] = (StatusCodes.Status409Conflict, "idempotency-response-not-replayable", "The response for this Idempotency-Key could not be safely replayed. Use a new key."),

            // S14-BE-01/02: CaptureBaselineCommand/ActivateBaselineCommand/CompareBaselineQuery.
            [BaselineErrorCodes.ProjectNotFound] = (StatusCodes.Status404NotFound, "not-found", "The requested resource was not found."),
            [BaselineErrorCodes.NotFound] = (StatusCodes.Status404NotFound, "not-found", "The requested resource was not found."),
            [BaselineErrorCodes.NoActiveBaseline] = (StatusCodes.Status422UnprocessableEntity, "baseline-no-active-baseline", "This project has no active baseline to compare against. Capture and activate a baseline first."),
            [BaselineErrorCodes.ActorRequired] = (StatusCodes.Status401Unauthorized, "baseline-actor-required", "The current request has no resolvable authenticated user."),
            // S14-QA concurrent-activation-race fix: also reached when a genuine two-request race
            // activates two different baselines for the same project at once (see
            // IBaselineRepository.TryActivateAsync's remarks) - wording deliberately says "this
            // project's active baseline", not "this baseline", since the loser's own target row was
            // never itself touched by the other request; it is the project's active-baseline pointer
            // that moved out from under it.
            [BaselineErrorCodes.ConcurrencyConflict] = (StatusCodes.Status409Conflict, "concurrent-transition", "Another action has already changed this project's active baseline. Reload and try again."),
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
