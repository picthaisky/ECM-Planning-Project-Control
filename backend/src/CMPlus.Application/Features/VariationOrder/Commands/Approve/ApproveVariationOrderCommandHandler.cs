using System.Text.Json;
using CMPlus.Application.Abstractions;
using CMPlus.Application.Approval;
using CMPlus.Domain.Common;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using MediatR;

namespace CMPlus.Application.Features.VariationOrder.Commands.Approve;

/// <summary>
/// S10-BE-03: the money-moving transition. Guards (a)-(c) - current-step role, self-approval,
/// <c>DuplicateChainVoter</c> - mirror <c>ApprovePaymentCertificateCommandHandler</c>'s H-01/H-02
/// shape exactly (see that handler's own remarks for the full rationale). What is new here, and only
/// evaluated when this vote would exhaust the chain (<see cref="Domain.Entities.VariationOrder.WouldFinalize"/>):
/// <list type="number">
/// <item>Guard (d), domain-rules.md §4.7: re-check the cumulative-VO escalation ratio against the
///   pinned policy - fail closed with 409 <see cref="VariationOrderErrorCodes.EscalationThresholdCrossedSinceSubmission"/>
///   rather than silently approving with no Executive signature anywhere. Reuses
///   <see cref="IApprovalRoutingService.ResolvePinned"/> (the exact Sprint 2 band/escalation formula,
///   not a second implementation) against the SINGLE pinned policy version, never the tenant's
///   currently-active one (H-01 discipline extended to this new guard) - and, since sprint-10's H-01
///   fix, never re-applies policy SELECTION (the <c>IsActive</c>/effective-window predicate) to that
///   already-chosen policy either, so a policy edit that deactivates it between submission and this
///   re-check can no longer silently substitute the permissive fallback chain
///   (docs/security/reviews/sprint-10.md).</item>
/// <item>domain-rules.md §5.1's non-negativity guard: 422 <see cref="VariationOrderErrorCodes.WouldMakeContractValueNegative"/>
///   if the signed <c>Amount</c> would drive <c>Project.BAC</c> or <c>Project.ContractValue</c> below
///   zero.</item>
/// <item>domain-rules.md §5.2: per scope line, 422 <see cref="VariationOrderErrorCodes.OmitsExecutedScope"/>
///   if a negative delta targets an activity with <c>ProgressPct &gt; 0</c>, and 422
///   <see cref="VariationOrderErrorCodes.ScopeActivityBudgetWouldBeNegative"/> if the delta would
///   drive that one activity's own budget below zero.</item>
/// </list>
/// All four are checked - and any failure returned - BEFORE <see cref="Domain.Entities.VariationOrder.Approve"/>
/// is ever called, so a blocked final vote leaves the document (and every Activity/Project figure)
/// byte-for-byte unchanged: no <c>ApprovalAction</c> row, no <c>LastVoteAt</c> stamp, nothing to undo
/// (domain-rules.md §4.7's V-6 step 5: "VO-B stays PendingApproval 2/2").
///
/// <para><b>The BAC invariant (domain-rules.md §5.2, the subtlest rule in this sprint):</b>
/// <c>Project.BAC</c> is a stored scalar, but <c>PV</c>/<c>EV</c> are sums over
/// <see cref="Activity.BudgetCost"/>. <see cref="Project.ApplyVariationOrderApproval"/> moves
/// <c>BAC</c>/<c>ContractValue</c> by the VO's signed <c>Amount</c>; this handler moves every scope
/// line's <see cref="Activity.BudgetCost"/> by its own <c>BudgetCostDelta</c> in the SAME unit of
/// work - and <see cref="Domain.Entities.VariationOrder"/>'s own constructor/<c>SetVariationContent</c>
/// already guarantee $\sum \text{BudgetCostDelta} = Amount$ to the cent (frozen since Submit), so the
/// two moves can never disagree.</para>
///
/// <para><b>Atomicity (domain-rules.md §5.1):</b> the status change, the scope-payload budget deltas,
/// the <c>Project</c> update, the <c>ApprovalAction</c> row and the in-flight-certificate disclosure
/// all commit in ONE <see cref="IVariationOrderRepository.TrySaveChangesAsync"/> call on the same
/// scoped <c>DbContext</c> - an <c>Approved</c> VO whose BAC move did not land is structurally
/// impossible.</para>
///
/// <para><b>Zero <see cref="ProjectFinanceLedger"/> rows, absolutely (domain-rules.md §6.4):</b> this
/// handler never constructs a <see cref="ProjectFinanceLedger"/> and never calls
/// <see cref="IPaymentCertificateRepository.AddLedgerEntries"/> - <see cref="IPaymentCertificateRepository"/>
/// is held only for <see cref="IPaymentCertificateRepository.ListByProjectAsync"/>'s read (§6.2's
/// in-flight-certificate disclosure). Unlike the scope/BAC guards above, this is not enforced by a
/// narrower injected capability (the interface is shared with <c>ApprovePaymentCertificateCommandHandler</c>,
/// which legitimately does call <c>AddLedgerEntries</c>) - it is verified by
/// <c>ApproveVariationOrderCommandHandlerTests</c>'s zero-ledger-row assertions and by a dedicated
/// integration test counting real <see cref="ProjectFinanceLedger"/> rows before/after, both of which
/// this task's mutation testing exercised (see the handoff report).</para>
///
/// <para><b>CPM re-trigger - deliberately not wired (disclosed deviation).</b> The DoD literally reads
/// "new activities become schedulable + trigger CPM recalculation", but Sprint 10's scope payload
/// (<see cref="VariationOrderScopeItem"/>'s own remarks) can only adjust the budget of EXISTING
/// activities - it cannot introduce a new one - so there is never a new activity to schedule this
/// sprint, and a <c>BudgetCost</c>-only change does not alter any CPM input (<c>DurationDays</c> or
/// relations). Calling <c>RecalculateCpmCommand</c> here would therefore always be an expensive no-op
/// against this sprint's actual scope shape. Flagged for `system-architect`/`qa-engineer` rather than
/// silently wired or silently dropped - see this task's handoff report.</para>
///
/// <para><b>Deferred (disclosed): the in-flight IPC "stale contract value" 409 guard (domain-rules.md
/// §6.3).</b> This handler DOES write the mandatory disclosure (§6.2:
/// <c>VoApprovedWithCertificateInFlight</c> audit rows for every <c>PendingApproval</c> certificate on
/// this project). It does NOT add the <c>CertificateStaleAgainstContractValue</c> guard on the
/// CERTIFICATE's own final approval (§6.3) - that requires snapshotting <c>ContractValue</c> onto
/// <see cref="PaymentCertificate"/> at ITS OWN Submit time, a schema/aggregate change to the Sprint 9
/// type that is out of scope for a clean, reviewable slice of this handler. See this task's handoff
/// report.</para>
/// </summary>
public sealed class ApproveVariationOrderCommandHandler(
    IVariationOrderRepository repository,
    IProjectRepository projectRepository,
    IApprovalPolicyReader policyReader,
    IApprovalRoutingService routingService,
    IApprovalActionRepository actionRepository,
    IPaymentCertificateRepository paymentCertificateRepository,
    ITenantProvider tenantProvider,
    ICurrentUserContext currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<ApproveVariationOrderCommand, Result<VariationOrderDto>>
{
    public async Task<Result<VariationOrderDto>> Handle(ApproveVariationOrderCommand request, CancellationToken cancellationToken)
    {
        var variationOrder = await repository.FindAsync(request.VariationOrderId, cancellationToken);
        if (variationOrder is null)
        {
            return Result<VariationOrderDto>.Failure(VariationOrderErrorCodes.NotFound);
        }

        if (variationOrder.Status != VariationOrderStatus.PendingApproval)
        {
            return Result<VariationOrderDto>.Failure(VariationOrderErrorCodes.InvalidStatusForTransition);
        }

        // H-01 fix: resolved from the chain snapshotted onto THIS document at Submit time - never
        // re-derived from the pinned policy's full rule set. See ApprovePaymentCertificateCommandHandler's
        // identical remark.
        var currentStep = variationOrder.ApprovalSteps.FirstOrDefault(
            s => s.RevisionNo == variationOrder.RevisionNo && s.StepNo == variationOrder.CurrentStepNo);
        if (currentStep is null)
        {
            return Result<VariationOrderDto>.Failure(VariationOrderErrorCodes.CorruptApprovalChain);
        }

        // L-01 (domain-rules.md §7.5): fail closed on a null actor rather than stamping Guid.Empty -
        // structurally unreachable behind [Authorize] but never silently accepted here either.
        if (currentUser.UserId is not { } actorUserId)
        {
            return Result<VariationOrderDto>.Failure(VariationOrderErrorCodes.ActorRequired);
        }

        var actorRole = currentUser.Role;

        if (actorRole != currentStep.RequiredRole)
        {
            return Result<VariationOrderDto>.Failure(VariationOrderErrorCodes.NotAuthorizedForApprovalStep);
        }

        if (!variationOrder.AllowSelfApproval && (actorUserId == variationOrder.CreatedByUserId || actorUserId == variationOrder.SubmittedByUserId))
        {
            return Result<VariationOrderDto>.Failure(VariationOrderErrorCodes.SelfApprovalNotPermitted);
        }

        // ADR-0016 / domain-rules.md §8.3: DuplicateChainVoter - an actor who already voted (Approve
        // OR Reject) this revision may not vote again, on any step.
        var history = await actionRepository.GetHistoryAsync(ApprovalDocumentType.VariationOrder, variationOrder.Id, cancellationToken);
        var alreadyVotedThisRevision = history.Any(a =>
            a.RevisionNo == variationOrder.RevisionNo
            && a.ActorUserId == actorUserId
            && (a.Action == ApprovalActionType.Approve || a.Action == ApprovalActionType.Reject));
        if (alreadyVotedThisRevision)
        {
            return Result<VariationOrderDto>.Failure(VariationOrderErrorCodes.DuplicateChainVoter);
        }

        // H-02 shape: a step clears only once QuorumCount DISTINCT users have approved THAT STEP.
        var priorDistinctApproversForThisStep = history
            .Where(a => a.RevisionNo == variationOrder.RevisionNo && a.StepNo == variationOrder.CurrentStepNo && a.Action == ApprovalActionType.Approve)
            .Select(a => a.ActorUserId)
            .Distinct()
            .Count();
        var quorumSatisfied = priorDistinctApproversForThisStep + 1 >= currentStep.QuorumCount;

        // "Immediately before the transition to Approved" (domain-rules.md §4.7) - never on an
        // intermediate vote, which would thrash and is not what the threshold is about.
        var wouldFinalize = variationOrder.WouldFinalize(quorumSatisfied);

        var project = await projectRepository.FindAsync(variationOrder.ProjectId, cancellationToken);
        if (project is null)
        {
            return Result<VariationOrderDto>.Failure(VariationOrderErrorCodes.ProjectNotFound);
        }

        decimal? cumulativeVoPctAtApproval = null;
        decimal? escalationBasisContractValue = null;
        IReadOnlyList<(Activity Activity, decimal Delta)>? scopeApplications = null;

        if (wouldFinalize)
        {
            // ---- Guard (d): the escalation-bypass re-check (domain-rules.md §4.7) ----
            var escalationCheck = await CheckEscalationBypassAsync(variationOrder, project, cancellationToken);
            if (escalationCheck.IsFailure)
            {
                return Result<VariationOrderDto>.Failure(escalationCheck.Error);
            }

            cumulativeVoPctAtApproval = escalationCheck.Value.CumulativeVoPctAtApproval;
            escalationBasisContractValue = escalationCheck.Value.EscalationBasisContractValue;

            // ---- domain-rules.md §5.1: non-negativity guard, checked before anything moves ----
            if (project.BAC + variationOrder.Amount < 0m || project.ContractValue + variationOrder.Amount < 0m)
            {
                return Result<VariationOrderDto>.Failure(VariationOrderErrorCodes.WouldMakeContractValueNegative);
            }

            // ---- domain-rules.md §5.2: per-scope-line guards ----
            var scopeCheck = await CheckScopeApplicationAsync(variationOrder, cancellationToken);
            if (scopeCheck.IsFailure)
            {
                return Result<VariationOrderDto>.Failure(scopeCheck.Error);
            }

            scopeApplications = scopeCheck.Value;
        }

        var now = clock.UtcNow;
        var bacBefore = project.BAC;
        var contractValueBefore = project.ContractValue;

        variationOrder.Approve(actorUserId, actorRole, currentStep.RequiredRole, variationOrder.AllowSelfApproval, now, quorumSatisfied);

        if (variationOrder.Status == VariationOrderStatus.Approved)
        {
            // ---- Effects (domain-rules.md §5): both money moves, the scope-budget deltas, and this
            // document's own immutable before/after stamp, all in the same unit of work. ----
            foreach (var (activity, delta) in scopeApplications!)
            {
                activity.AdjustBudgetCost(delta);
            }

            project.ApplyVariationOrderApproval(variationOrder.Amount, now);

            variationOrder.RecordApprovalEffects(
                bacBefore, project.BAC, contractValueBefore, project.ContractValue,
                cumulativeVoPctAtApproval, escalationBasisContractValue);

            // domain-rules.md §6.2: mandatory disclosure, never silent. The certificate itself is
            // NOT recomputed (§6.1) - this is audit evidence only.
            await DiscloseToInFlightCertificatesAsync(variationOrder, contractValueBefore, project.ContractValue, actorUserId, now, cancellationToken);
        }

        actionRepository.Add(new ApprovalAction(
            tenantProvider.TenantId,
            ApprovalDocumentType.VariationOrder,
            variationOrder.Id,
            variationOrder.RevisionNo,
            currentStep.StepNo,
            actorUserId,
            actorRole,
            ApprovalActionType.Approve,
            request.Comment,
            now,
            variationOrder.ApprovalPolicyId ?? Guid.Empty,
            variationOrder.ApprovalPolicyVersion ?? 0));

        if (!await repository.TrySaveChangesAsync(cancellationToken))
        {
            return Result<VariationOrderDto>.Failure(VariationOrderErrorCodes.ConcurrencyConflict);
        }

        return Result<VariationOrderDto>.Success(VariationOrderDto.From(variationOrder));
    }

    /// <summary>
    /// domain-rules.md §4.7. Re-evaluates the SAME formula <see cref="IApprovalRoutingService.Resolve"/>
    /// already applied at Submit time, against the SAME pinned policy version (fetched by id - never
    /// re-selected from "whatever is active now", which could have changed) and this VO's own
    /// unchanged (frozen since Submit) <c>Amount</c>, but a FRESH $\Sigma^{VO}$. Reusing <c>Resolve</c>
    /// wholesale - rather than re-implementing the ratio/threshold comparison a second time - is what
    /// guarantees this guard can never numerically disagree with the routing engine it is re-checking
    /// (single source of truth for "how is Phi computed and compared").
    /// </summary>
    private async Task<Result<(decimal? CumulativeVoPctAtApproval, decimal? EscalationBasisContractValue)>> CheckEscalationBypassAsync(
        Domain.Entities.VariationOrder variationOrder, Project project, CancellationToken cancellationToken)
    {
        // Guid.Empty / null = the "no policy configured at all" fallback chain (a single hard-coded
        // ProjectDirector step) - there is no real ApprovalPolicy row to carry a threshold, so no
        // escalation concept applies to it at all (approval-workflow.md §5.3 step 6).
        if (variationOrder.ApprovalPolicyId is not { } policyId || policyId == Guid.Empty)
        {
            return Result<(decimal?, decimal?)>.Success((null, null));
        }

        var pinnedPolicy = await policyReader.GetByIdAsync(policyId, cancellationToken);
        if (pinnedPolicy is null)
        {
            // Should be unreachable: ApprovalPolicy rows are only ever deactivated, never deleted
            // (approval-workflow.md §5.2 "version-pin, never mutate"). Defense-in-depth, same M-03
            // "degrade to a mapped 409, never an unhandled exception" discipline used elsewhere.
            return Result<(decimal?, decimal?)>.Failure(VariationOrderErrorCodes.CorruptApprovalChain);
        }

        if (pinnedPolicy.CumulativeVoEscalationPct is null)
        {
            // No threshold configured on the pinned policy - nothing to re-check, nothing to record.
            return Result<(decimal?, decimal?)>.Success((null, null));
        }

        // Sigma^VO EXCLUDING this VO (it is not yet Approved) - the routing service adds this VO's own
        // Amount internally, exactly as it did at Submit (ApprovalRoutingRequest.CumulativeApprovedVoAmount's
        // own remarks: "The VO currently being submitted is NOT included here").
        var currentApprovedTotal = await repository.GetApprovedNetSignedTotalAsync(variationOrder.ProjectId, cancellationToken);

        // H-01 fix (sprint-10 security review): ResolvePinned, never Resolve. pinnedPolicy has ALREADY
        // been chosen (it is the exact version that routed this document at Submit) - routing it back
        // through Resolve's SelectPolicy (as a lone "candidate") would re-apply the IsActive/effective-
        // window predicate to an already-chosen policy, and an ordinary UpdateApprovalPolicyCommandHandler
        // edit deactivates the current version on every save. That combination silently substituted the
        // permissive, no-Executive FallbackApprovalChain for this guard's re-check - a routine policy
        // edit re-opening exactly the escalation bypass this guard exists to close (docs/security/reviews/
        // sprint-10.md H-01). CandidatePolicies is unused by ResolvePinned but still supplied so the
        // record can be shared with SubmitVariationOrderCommandHandler's shape.
        var recheck = routingService.ResolvePinned(pinnedPolicy, new ApprovalRoutingRequest(
            ApprovalDocumentType.VariationOrder,
            variationOrder.ProjectId,
            variationOrder.Amount,
            variationOrder.SubmittedAt!.Value, // the policy's effective-window selection as of ORIGINAL submission, not "now"
            [pinnedPolicy],
            EscalationBaselineContractValue: project.EscalationBaselineContractValue,
            CumulativeApprovedVoAmount: currentApprovedTotal));

        if (recheck.IsFailure)
        {
            // ApprovalErrorCodes.ContractValueNotConfigured - realistically unreachable this sprint
            // (nothing moves Project.OriginalContractValue yet), but fail closed rather than silently
            // approving if it ever is.
            return Result<(decimal?, decimal?)>.Failure(recheck.Error);
        }

        var escalationRole = pinnedPolicy.CumulativeVoEscalationRole;
        var freshChainRequiresRole = escalationRole is not null && recheck.Value.Steps.Any(s => s.RequiredRole == escalationRole);
        var snapshottedChainAlreadyHasRole = escalationRole is not null && variationOrder.ApprovalSteps.Any(
            s => s.RevisionNo == variationOrder.RevisionNo && s.RequiredRole == escalationRole);

        // domain-rules.md §4.2's exact expression, reproduced only for the STORED audit value (the
        // BLOCKING decision above already came from the reused Resolve() call, not from this line) -
        // identical operand order/precision to ApprovalRoutingService's own ratioPct so the two can
        // never disagree even though this one line is not physically shared code.
        var cumulativeVoPctAtApproval = (currentApprovedTotal + variationOrder.Amount) / project.EscalationBaselineContractValue * 100m;

        if (freshChainRequiresRole && !snapshottedChainAlreadyHasRole)
        {
            return Result<(decimal?, decimal?)>.Failure(VariationOrderErrorCodes.EscalationThresholdCrossedSinceSubmission);
        }

        return Result<(decimal?, decimal?)>.Success((cumulativeVoPctAtApproval, project.EscalationBaselineContractValue));
    }

    /// <summary>
    /// domain-rules.md §5.2. Loads every scope-line target <see cref="Activity"/> (tracked, on this
    /// same unit of work) and validates BOTH per-line guards for EVERY line before returning any of
    /// them - all-or-nothing, so a failure on line 3 never leaves lines 1-2 partially staged.
    /// </summary>
    private async Task<Result<IReadOnlyList<(Activity Activity, decimal Delta)>>> CheckScopeApplicationAsync(
        Domain.Entities.VariationOrder variationOrder, CancellationToken cancellationToken)
    {
        if (variationOrder.ScopeItems.Count == 0)
        {
            return Result<IReadOnlyList<(Activity, decimal)>>.Success([]);
        }

        var activityIds = variationOrder.ScopeItems.Select(i => i.ActivityId).Distinct().ToList();
        var activities = await repository.FindActivitiesForApprovalAsync(variationOrder.ProjectId, activityIds, cancellationToken);
        var activitiesById = activities.ToDictionary(a => a.Id);

        var applications = new List<(Activity Activity, decimal Delta)>(variationOrder.ScopeItems.Count);

        foreach (var item in variationOrder.ScopeItems)
        {
            if (!activitiesById.TryGetValue(item.ActivityId, out var activity))
            {
                // Defense-in-depth: unreachable today (no Activity delete path exists anywhere in this
                // codebase), but a scope line that no longer resolves must never be silently skipped.
                return Result<IReadOnlyList<(Activity, decimal)>>.Failure(VariationOrderErrorCodes.UnknownActivity);
            }

            if (item.BudgetCostDelta < 0m && activity.ProgressPercentage > 0m)
            {
                return Result<IReadOnlyList<(Activity, decimal)>>.Failure(VariationOrderErrorCodes.OmitsExecutedScope);
            }

            if (activity.BudgetCost + item.BudgetCostDelta < 0m)
            {
                return Result<IReadOnlyList<(Activity, decimal)>>.Failure(VariationOrderErrorCodes.ScopeActivityBudgetWouldBeNegative);
            }

            applications.Add((activity, item.BudgetCostDelta));
        }

        return Result<IReadOnlyList<(Activity, decimal)>>.Success(applications);
    }

    /// <summary>
    /// domain-rules.md §6.2: "the handler must find every IPC for that project with
    /// Status = PendingApproval and, for each: write an AuditLog entry VoApprovedWithCertificateInFlight
    /// carrying both document ids, the VO amount, and C^cur before -&gt; after." The certificate itself
    /// is never touched (§6.1 is unconditional) - this writes evidence only.
    /// </summary>
    private async Task DiscloseToInFlightCertificatesAsync(
        Domain.Entities.VariationOrder variationOrder,
        decimal contractValueBefore,
        decimal contractValueAfter,
        Guid actorUserId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var certificates = await paymentCertificateRepository.ListByProjectAsync(variationOrder.ProjectId, cancellationToken);
        var inFlight = certificates.Where(c => c.Status == PaymentCertificateStatus.PendingApproval).ToList();

        foreach (var certificate in inFlight)
        {
            repository.AddAuditLogEntry(new AuditLog(
                tenantProvider.TenantId,
                "VoApprovedWithCertificateInFlight",
                certificate.Id,
                AuditAction.Updated,
                actorUserId,
                beforeJson: null,
                afterJson: JsonSerializer.Serialize(new
                {
                    VariationOrderId = variationOrder.Id,
                    variationOrder.VoNumber,
                    variationOrder.Amount,
                    PaymentCertificateId = certificate.Id,
                    ContractValueBefore = contractValueBefore,
                    ContractValueAfter = contractValueAfter,
                }),
                now));
        }
    }
}
