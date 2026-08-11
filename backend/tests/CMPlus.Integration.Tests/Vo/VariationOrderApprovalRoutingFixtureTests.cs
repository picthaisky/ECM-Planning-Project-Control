using CMPlus.Application.Approval;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Integration.Tests.Vo;

/// <summary>
/// S10-BE-02: domain-rules.md §3.4's R1-R6 fixtures exercised through the real
/// <c>SubmitVariationOrderCommandHandler</c>/<c>ApproveVariationOrderCommandHandler</c> against a real
/// <c>CmPlusDbContext</c> (EF Core InMemory, per the Docker outage) - not the pure
/// routing-service-only slice <c>ApprovalRoutingServiceTests</c>/<c>RoutingFixtureTests</c> already
/// cover (Sprint 2/10 unit level). This proves the routing/state-machine logic end to end at the
/// handler layer: the real handler, the real tenant-scoped EF Core repositories, the real
/// interceptors - the same production wiring <c>ApprovalWorkflowHarness</c> established for Sprint
/// 9's R7-R10.
///
/// <para><b>Correction (S10-QA-01 independent verification, 2026-08-10).</b> This file's own doc
/// comment previously claimed that the coverage above <i>is</i> what S10-QA-01's DoD means by
/// "ผ่านครบผ่าน API จริง (ไม่ใช่แค่ unit ของ service)" - pass completely through the real API, not merely
/// a unit of the service. That was wrong, and `qa-engineer` correctly read it as a self-serving
/// redefinition: this class never constructs an <c>HttpClient</c>, never goes through
/// <c>VariationOrdersController</c>/<c>ProjectVariationOrdersController</c>, JWT authentication, RBAC,
/// or JSON model binding - and the gap was real, proven by mutation
/// (<c>VariationOrder.ApplyContent</c>'s <c>Amount = amount</c> -&gt; <c>Amount = Math.Abs(amount)</c>,
/// which turns every Deduct VO into an Add and moves BAC up instead of down, left the then-existing
/// HTTP suite 10/10 green because its only BAC-moving test used a positive amount; separately, no
/// HTTP test seeded a cumulative-VO-escalation policy at all, so neutering the escalation-bypass
/// guard - domain-rules.md §4.7 - also left it green). R1, R2 (Deduct), R4 (escalation), R5 (422
/// gap), R6 (exact boundary) and the V-6 escalation-bypass race now additionally have real-HTTP
/// coverage in <c>CMPlus.Integration.Tests.WebApi.VariationOrdersControllerTests</c>, verified by
/// re-running both mutations against that class and confirming it goes red. THAT class - not this
/// one - is what actually satisfies "ผ่านครบผ่าน API จริง"; this class remains valuable as the
/// handler-level slice (faster, easier to pinpoint a routing defect in), not a substitute for it.
/// </para>
/// </summary>
public class VariationOrderApprovalRoutingFixtureTests
{
    private static readonly DateTimeOffset EffectiveFrom = DateTimeOffset.Parse("2025-01-01T00:00:00+07:00");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-10T09:00:00+07:00");

    [Fact]
    public async Task R1_Add_2_4M_With_Low_Cumulative_Resolves_PM_Then_ProjectDirector_No_Escalation()
    {
        var harness = new VariationOrderWorkflowHarness(now: Now);
        await harness.SeedDefaultApprovalPoliciesAsync(EffectiveFrom); // TH-Default-VO, CumulativeVoEscalationPct = NULL
        var projectId = await harness.SeedProjectAsync(bac: 485_000_000.00m);
        var creatorId = Guid.NewGuid();
        var voId = await harness.CreateDraftAsync(projectId, 2_400_000.00m, creatorId);

        var result = await harness.SubmitAsync(voId, creatorId, UserRole.QS);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
        Assert.Equal(2, result.Value.TotalSteps);
        Assert.Equal([UserRole.PM, UserRole.ProjectDirector], result.Value.ApprovalSteps.OrderBy(s => s.StepNo).Select(s => s.RequiredRole));
    }

    /// <summary>
    /// R2, the load-bearing fixture (domain-rules.md §3.4): three assertions against the REAL,
    /// persisted row, round-tripped through a fresh <see cref="Infrastructure.Persistence.CmPlusDbContext"/>
    /// read (<see cref="VariationOrderWorkflowHarness.LoadAsync"/>), not merely the in-memory handler
    /// return value. (i) the routing amount used to select the chain is positive; (ii) the chain is
    /// byte-identical to a twin +800,000.00 Add's; (iii) the PERSISTED <c>Amount</c> column is still
    /// exactly -800,000.00.
    /// </summary>
    [Fact]
    public async Task R2_Deduct_800k_Persists_A_Negative_Amount_And_Resolves_The_Same_Chain_As_A_Plus_800k_Add()
    {
        var harness = new VariationOrderWorkflowHarness(now: Now);
        await harness.SeedDefaultApprovalPoliciesAsync(EffectiveFrom);
        var projectId = await harness.SeedProjectAsync(bac: 485_000_000.00m);
        var creatorId = Guid.NewGuid();

        var deductVoId = await harness.CreateDraftAsync(projectId, -800_000.00m, creatorId);
        var deductResult = await harness.SubmitAsync(deductVoId, creatorId, UserRole.QS);
        Assert.True(deductResult.IsSuccess, deductResult.IsFailure ? deductResult.Error : string.Empty);

        var persisted = await harness.LoadAsync(deductVoId);
        Assert.Equal(-800_000.00m, persisted.Amount); // (iii) - never overwritten to abs()
        Assert.Equal(VariationOrderType.Deduct, persisted.Type);

        var deductChain = persisted.ApprovalSteps.OrderBy(s => s.StepNo).Select(s => s.RequiredRole).ToList();
        Assert.Equal([UserRole.PM, UserRole.ProjectDirector], deductChain); // (i)/(ii) implicit: positive-magnitude band

        var addVoId = await harness.CreateDraftAsync(projectId, 800_000.00m, creatorId);
        var addResult = await harness.SubmitAsync(addVoId, creatorId, UserRole.QS);
        Assert.True(addResult.IsSuccess);
        var addChain = (await harness.LoadAsync(addVoId)).ApprovalSteps.OrderBy(s => s.StepNo).Select(s => s.RequiredRole).ToList();

        Assert.Equal(addChain, deductChain); // (ii) byte-identical chain to the twin +800,000.00 control
    }

    [Fact]
    public async Task R3_Add_300k_Resolves_Single_Step_Pm()
    {
        var harness = new VariationOrderWorkflowHarness(now: Now);
        await harness.SeedDefaultApprovalPoliciesAsync(EffectiveFrom);
        var projectId = await harness.SeedProjectAsync(bac: 485_000_000.00m);
        var creatorId = Guid.NewGuid();
        var voId = await harness.CreateDraftAsync(projectId, 300_000.00m, creatorId);

        var result = await harness.SubmitAsync(voId, creatorId, UserRole.QS);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.ApprovalSteps);
        Assert.Equal(UserRole.PM, result.Value.ApprovalSteps[0].RequiredRole);
    }

    [Fact]
    public async Task R4_Escalation_Appends_Executive_When_Cumulative_Exceeds_10_Percent()
    {
        var harness = new VariationOrderWorkflowHarness(now: Now);
        var projectId = await harness.SeedProjectAsync(bac: 485_000_000.00m, originalContractValue: 485_000_000.00m);
        await harness.SeedEscalationPolicyAsync(projectId, EffectiveFrom);

        // Sigma_prior = 46,000,000.00 already Approved. Every VO that reaches full approval below
        // needs a REAL seeded Activity for its scope line - the final approval's scope-application
        // guard (domain-rules.md §5.2) queries Activities/WBSNodes for real, unlike Submit.
        var creatorId = Guid.NewGuid();
        var priorActivityId = await harness.SeedActivityAsync(projectId, budgetCost: 900_000_000.00m);
        var priorVoId = await harness.CreateDraftAsync(
            projectId, 46_000_000.00m, creatorId, [new VariationOrderScopeItemInput(priorActivityId, 46_000_000.00m)]);
        var priorSubmit = await harness.SubmitAsync(priorVoId, creatorId, UserRole.QS);
        Assert.True(priorSubmit.IsSuccess);
        // 46,000,000.00 is itself >= 5,000,000.00, so the BAND ALONE already requires all three
        // rungs (PM, ProjectDirector, Executive - TH-Default-VO's own >=5M band shape), independent
        // of escalation - three approvals are needed to reach Approved, not two.
        Assert.Equal(3, priorSubmit.Value.TotalSteps);
        var priorPmApprove = await harness.ApproveAsync(priorVoId, Guid.NewGuid(), UserRole.PM);
        Assert.True(priorPmApprove.IsSuccess);
        var priorPdApprove = await harness.ApproveAsync(priorVoId, Guid.NewGuid(), UserRole.ProjectDirector);
        Assert.True(priorPdApprove.IsSuccess);
        var priorExecutiveApprove = await harness.ApproveAsync(priorVoId, Guid.NewGuid(), UserRole.Executive);
        Assert.True(priorExecutiveApprove.IsSuccess, priorExecutiveApprove.IsFailure ? priorExecutiveApprove.Error : string.Empty);
        Assert.Equal(VariationOrderStatus.Approved, priorExecutiveApprove.Value.Status);

        var activityId = await harness.SeedActivityAsync(projectId, budgetCost: 900_000_000.00m);
        var voId = await harness.CreateDraftAsync(
            projectId, 3_200_000.00m, creatorId, [new VariationOrderScopeItemInput(activityId, 3_200_000.00m)]);
        var result = await harness.SubmitAsync(voId, creatorId, UserRole.QS);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
        Assert.Equal(3, result.Value.TotalSteps);
        Assert.Equal(UserRole.Executive, result.Value.ApprovalSteps.OrderBy(s => s.StepNo).Last().RequiredRole);

        // H-01 inheritance (domain-rules.md §3.4's own warning): the escalated Executive step must be
        // fully APPROVABLE, not merely present in the snapshot - proving the synthesised rung is
        // representable end to end, not just resolved.
        var afterPm = await harness.ApproveAsync(voId, Guid.NewGuid(), UserRole.PM);
        Assert.True(afterPm.IsSuccess);
        var afterPd = await harness.ApproveAsync(voId, Guid.NewGuid(), UserRole.ProjectDirector);
        Assert.True(afterPd.IsSuccess);
        var afterExecutive = await harness.ApproveAsync(voId, Guid.NewGuid(), UserRole.Executive);
        Assert.True(afterExecutive.IsSuccess, afterExecutive.IsFailure ? afterExecutive.Error : string.Empty);
        Assert.Equal(VariationOrderStatus.Approved, afterExecutive.Value.Status);
    }

    [Fact]
    public async Task R5_Below_The_Lowest_Configured_Band_Fails_Closed_With_ApprovalPolicyGap_And_Stays_Draft()
    {
        var harness = new VariationOrderWorkflowHarness(now: Now);
        var projectId = await harness.SeedProjectAsync(bac: 1_000_000.00m);
        await harness.SeedGapPolicyAsync(projectId, EffectiveFrom); // TH-Gap-VO: nothing covers [0, 100,000)
        var creatorId = Guid.NewGuid();
        var voId = await harness.CreateDraftAsync(projectId, 50_000.00m, creatorId);

        var result = await harness.SubmitAsync(voId, creatorId, UserRole.QS);

        Assert.True(result.IsFailure);
        Assert.Equal(ApprovalErrorCodes.PolicyGap, result.Error);
        var persisted = await harness.LoadAsync(voId);
        Assert.Equal(VariationOrderStatus.Draft, persisted.Status); // never auto-approved
        Assert.Empty(persisted.ApprovalSteps);
    }

    [Fact]
    public async Task R6_Boundary_Exactly_500000_Belongs_To_The_Upper_Band()
    {
        var harness = new VariationOrderWorkflowHarness(now: Now);
        await harness.SeedDefaultApprovalPoliciesAsync(EffectiveFrom);
        var projectId = await harness.SeedProjectAsync(bac: 485_000_000.00m);
        var creatorId = Guid.NewGuid();
        var voId = await harness.CreateDraftAsync(projectId, 500_000.00m, creatorId);

        var result = await harness.SubmitAsync(voId, creatorId, UserRole.QS);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.TotalSteps);
        Assert.Equal([UserRole.PM, UserRole.ProjectDirector], result.Value.ApprovalSteps.OrderBy(s => s.StepNo).Select(s => s.RequiredRole));
    }

    /// <summary>
    /// V-6, the full six-step race (domain-rules.md §4.8), through the real handlers/persistence:
    /// two VOs each submitted under-threshold can be independently approved and cross the cumulative
    /// threshold with no Executive signature anywhere - unless the final-approval guard closes it.
    /// </summary>
    [Fact]
    public async Task V6_The_Escalation_Bypass_Race_Is_Closed_By_The_Final_Approval_Guard_And_Recoverable_Via_ReturnForRevision()
    {
        var harness = new VariationOrderWorkflowHarness(now: Now);
        var projectId = await harness.SeedProjectAsync(bac: 485_000_000.00m, originalContractValue: 485_000_000.00m);
        await harness.SeedEscalationPolicyAsync(projectId, EffectiveFrom);
        var creatorId = Guid.NewGuid();

        // Sigma_prior = 44,000,000.00 - a pre-existing approved base, seeded directly (its own
        // submit/approve mechanics are exercised by R4/R1 above, not the point of this test). Every
        // VO that reaches full approval needs a real seeded Activity (see R4's identical note).
        var baseActivityId = await harness.SeedActivityAsync(projectId, budgetCost: 900_000_000.00m);
        var baseVoId = await harness.CreateDraftAsync(
            projectId, 44_000_000.00m, creatorId, [new VariationOrderScopeItemInput(baseActivityId, 44_000_000.00m)]);
        var baseSubmit = await harness.SubmitAsync(baseVoId, creatorId, UserRole.QS);
        Assert.True(baseSubmit.IsSuccess);
        // 44,000,000.00 is itself >= 5,000,000.00, so the BAND ALONE already requires all three
        // rungs (PM, ProjectDirector, Executive), independent of escalation.
        Assert.Equal(3, baseSubmit.Value.TotalSteps);
        Assert.True((await harness.ApproveAsync(baseVoId, Guid.NewGuid(), UserRole.PM)).IsSuccess);
        Assert.True((await harness.ApproveAsync(baseVoId, Guid.NewGuid(), UserRole.ProjectDirector)).IsSuccess);
        var baseFinal = await harness.ApproveAsync(baseVoId, Guid.NewGuid(), UserRole.Executive);
        Assert.True(baseFinal.IsSuccess, baseFinal.IsFailure ? baseFinal.Error : string.Empty);
        Assert.Equal(VariationOrderStatus.Approved, baseFinal.Value.Status);

        // Step 1: VO-A Add +2,400,000.00 - (44,000,000+2,400,000)/485,000,000 = 9.5670% -> no escalation.
        var voAActivityId = await harness.SeedActivityAsync(projectId, budgetCost: 900_000_000.00m);
        var voAId = await harness.CreateDraftAsync(
            projectId, 2_400_000.00m, creatorId, [new VariationOrderScopeItemInput(voAActivityId, 2_400_000.00m)]);
        var voASubmit = await harness.SubmitAsync(voAId, creatorId, UserRole.QS);
        Assert.True(voASubmit.IsSuccess);
        Assert.Equal(2, voASubmit.Value.TotalSteps);

        // Step 2: VO-B Add +2,300,000.00 - (44,000,000+2,300,000)/485,000,000 = 9.5464% -> no escalation.
        var voBId = await harness.CreateDraftAsync(projectId, 2_300_000.00m, creatorId);
        var voBSubmit = await harness.SubmitAsync(voBId, creatorId, UserRole.QS);
        Assert.True(voBSubmit.IsSuccess);
        Assert.Equal(2, voBSubmit.Value.TotalSteps);
        Assert.DoesNotContain(voBSubmit.Value.ApprovalSteps, s => s.RequiredRole == UserRole.Executive);

        // Step 3: VO-A fully approved -> Sigma^VO = 46,400,000.00.
        Assert.True((await harness.ApproveAsync(voAId, Guid.NewGuid(), UserRole.PM)).IsSuccess);
        var voAFinal = await harness.ApproveAsync(voAId, Guid.NewGuid(), UserRole.ProjectDirector);
        Assert.True(voAFinal.IsSuccess);
        Assert.Equal(VariationOrderStatus.Approved, voAFinal.Value.Status);

        // Step 4: VO-B: PM approves (step 1 -> 2) - not checked at non-final steps.
        var voBAfterPm = await harness.ApproveAsync(voBId, Guid.NewGuid(), UserRole.PM);
        Assert.True(voBAfterPm.IsSuccess);
        Assert.Equal(2, voBAfterPm.Value.CurrentStepNo);

        var projectBeforeBlockedAttempt = await harness.LoadProjectAsync(projectId);

        // Step 5: VO-B: ProjectDirector approves - final step. Re-check:
        // (46,400,000+2,300,000)/485,000,000 = 10.0412% > 10.00% -> BLOCKED, no Executive signature exists.
        var voBFinalAttempt = await harness.ApproveAsync(voBId, Guid.NewGuid(), UserRole.ProjectDirector);

        Assert.True(voBFinalAttempt.IsFailure);
        Assert.Equal("VoEscalationThresholdCrossedSinceSubmission", voBFinalAttempt.Error);
        var voBAfterBlock = await harness.LoadAsync(voBId);
        Assert.Equal(VariationOrderStatus.PendingApproval, voBAfterBlock.Status); // stays PendingApproval 2/2
        Assert.Equal(2, voBAfterBlock.CurrentStepNo);
        Assert.Equal(2, voBAfterBlock.TotalSteps);
        Assert.DoesNotContain(voBAfterBlock.ApprovalSteps, s => s.RequiredRole == UserRole.Executive); // chain never mutated in place
        var projectAfterBlockedAttempt = await harness.LoadProjectAsync(projectId);
        Assert.Equal(projectBeforeBlockedAttempt.BAC, projectAfterBlockedAttempt.BAC); // no BAC change

        // Step 6: PD returns VO-B for revision; QS resubmits at the same +2,300,000.00 -> chain
        // re-resolves to [PM, PD, Executive].
        var returnResult = await harness.ReturnForRevisionAsync(voBId, Guid.NewGuid(), UserRole.ProjectDirector, "Escalation threshold crossed - resubmitting.");
        Assert.True(returnResult.IsSuccess);
        Assert.Equal(2, returnResult.Value.RevisionNo);

        var resubmitResult = await harness.SubmitAsync(voBId, creatorId, UserRole.QS);
        Assert.True(resubmitResult.IsSuccess, resubmitResult.IsFailure ? resubmitResult.Error : string.Empty);
        Assert.Equal(3, resubmitResult.Value.TotalSteps);
        Assert.Equal(UserRole.Executive, resubmitResult.Value.ApprovalSteps.OrderBy(s => s.StepNo).Last().RequiredRole);
    }

    /// <summary>
    /// H-01 (docs/security/reviews/sprint-10.md), the review's own requested follow-up: "Both existing
    /// V-6 tests keep the policy active. Add a V-6 variant that versions the policy between VO-A's
    /// approval and VO-B's final vote, and assert the 409 still fires." Identical to
    /// <see cref="V6_The_Escalation_Bypass_Race_Is_Closed_By_The_Final_Approval_Guard_And_Recoverable_Via_ReturnForRevision"/>
    /// through step 4, except for the one line that used to matter: between VO-B's PM approval and its
    /// final Project-Director vote, an ordinary Admin edit deactivates the pinned policy - exactly what
    /// <c>UpdateApprovalPolicyCommandHandler.cs:45</c> does, unconditionally, on every save. Through the
    /// real <c>ApproveVariationOrderCommandHandler</c>/<c>ApprovalPolicyReader</c>/
    /// <c>ApprovalRoutingService</c>, not a fake.
    /// </summary>
    [Fact]
    public async Task V6_Variant_The_Escalation_Guard_Still_Fires_Even_When_The_Pinned_Policy_Is_Deactivated_Between_Submission_And_The_Final_Vote_H01()
    {
        var harness = new VariationOrderWorkflowHarness(now: Now);
        var projectId = await harness.SeedProjectAsync(bac: 485_000_000.00m, originalContractValue: 485_000_000.00m);
        var policyId = await harness.SeedEscalationPolicyAsync(projectId, EffectiveFrom);
        var creatorId = Guid.NewGuid();

        // Sigma_prior = 44,000,000.00, identical setup to V6 above.
        var baseActivityId = await harness.SeedActivityAsync(projectId, budgetCost: 900_000_000.00m);
        var baseVoId = await harness.CreateDraftAsync(
            projectId, 44_000_000.00m, creatorId, [new VariationOrderScopeItemInput(baseActivityId, 44_000_000.00m)]);
        Assert.True((await harness.SubmitAsync(baseVoId, creatorId, UserRole.QS)).IsSuccess);
        Assert.True((await harness.ApproveAsync(baseVoId, Guid.NewGuid(), UserRole.PM)).IsSuccess);
        Assert.True((await harness.ApproveAsync(baseVoId, Guid.NewGuid(), UserRole.ProjectDirector)).IsSuccess);
        var baseFinal = await harness.ApproveAsync(baseVoId, Guid.NewGuid(), UserRole.Executive);
        Assert.True(baseFinal.IsSuccess, baseFinal.IsFailure ? baseFinal.Error : string.Empty);

        // VO-A Add +2,400,000.00 SUBMITTED first, while Sigma_prior is still 44,000,000.00 - chain
        // [PM, ProjectDirector], no escalation ((44,000,000+2,400,000)/485,000,000 = 9.5670%).
        var voAActivityId = await harness.SeedActivityAsync(projectId, budgetCost: 900_000_000.00m);
        var voAId = await harness.CreateDraftAsync(
            projectId, 2_400_000.00m, creatorId, [new VariationOrderScopeItemInput(voAActivityId, 2_400_000.00m)]);
        var voASubmit = await harness.SubmitAsync(voAId, creatorId, UserRole.QS);
        Assert.True(voASubmit.IsSuccess);
        Assert.Equal(2, voASubmit.Value.TotalSteps);

        // VO-B Add +2,300,000.00 ALSO SUBMITTED before either VO advances - same Sigma_prior =
        // 44,000,000.00 -> chain [PM, ProjectDirector], no escalation
        // ((44,000,000+2,300,000)/485,000,000 = 9.5464%). Submitting VO-B only after VO-A had already
        // been fully approved would make VO-B's OWN submission see Sigma_prior = 46,400,000.00 and
        // escalate immediately at Submit - masking the bypass this test exists to reproduce, exactly
        // as domain-rules.md §4.8's V-6 fixture requires ("two VOs each submitted under-threshold").
        // Given a REAL seeded scope item (not a phantom ActivityId) so that a mutation which removes
        // the H-01 fix surfaces as the true bypass (final approval succeeds with no Executive) rather
        // than an unrelated VariationOrderUnknownActivity from the scope-application guard that would
        // otherwise mask it once the escalation guard stopped blocking.
        var voBActivityId = await harness.SeedActivityAsync(projectId, budgetCost: 900_000_000.00m);
        var voBId = await harness.CreateDraftAsync(
            projectId, 2_300_000.00m, creatorId, [new VariationOrderScopeItemInput(voBActivityId, 2_300_000.00m)]);
        var voBSubmit = await harness.SubmitAsync(voBId, creatorId, UserRole.QS);
        Assert.True(voBSubmit.IsSuccess);
        Assert.Equal(2, voBSubmit.Value.TotalSteps);

        // VO-A fully approved -> Sigma^VO = 46,400,000.00.
        Assert.True((await harness.ApproveAsync(voAId, Guid.NewGuid(), UserRole.PM)).IsSuccess);
        var voAFinal = await harness.ApproveAsync(voAId, Guid.NewGuid(), UserRole.ProjectDirector);
        Assert.True(voAFinal.IsSuccess);
        Assert.Equal(VariationOrderStatus.Approved, voAFinal.Value.Status);

        // VO-B: PM approves (step 1 -> 2) - not checked at non-final steps.
        var voBAfterPm = await harness.ApproveAsync(voBId, Guid.NewGuid(), UserRole.PM);
        Assert.True(voBAfterPm.IsSuccess);
        Assert.Equal(2, voBAfterPm.Value.CurrentStepNo);

        // *** THE ONLY DIFFERENCE FROM V6 ABOVE ***: an ordinary policy edit lands here, between VO-B's
        // PM approval and its final Project-Director vote - deactivating the SAME policy VO-B pinned
        // at Submit. Before H-01's fix, this silently substituted the permissive FallbackChain (no
        // Executive) for the escalation re-check and the bypass succeeded.
        await harness.DeactivatePolicyAsync(policyId);

        var projectBeforeFinalAttempt = await harness.LoadProjectAsync(projectId);

        // Final step: ProjectDirector approves. Re-check: (46,400,000+2,300,000)/485,000,000 =
        // 10.0412% > 10.00% -> MUST still be BLOCKED, exactly as it is when the policy stays active.
        var voBFinalAttempt = await harness.ApproveAsync(voBId, Guid.NewGuid(), UserRole.ProjectDirector);

        Assert.True(voBFinalAttempt.IsFailure);
        Assert.Equal("VoEscalationThresholdCrossedSinceSubmission", voBFinalAttempt.Error);
        var voBAfterBlock = await harness.LoadAsync(voBId);
        Assert.Equal(VariationOrderStatus.PendingApproval, voBAfterBlock.Status);
        Assert.Equal(2, voBAfterBlock.CurrentStepNo);
        Assert.DoesNotContain(voBAfterBlock.ApprovalSteps, s => s.RequiredRole == UserRole.Executive);
        var projectAfterFinalAttempt = await harness.LoadProjectAsync(projectId);
        Assert.Equal(projectBeforeFinalAttempt.BAC, projectAfterFinalAttempt.BAC); // no BAC change - no bypass

        // The remedy still works even with the pinned policy deactivated: the guard's own fail-closed
        // 409 is recoverable via the ordinary ReturnForRevision escape (domain-rules.md §4.7 - "the
        // remedy is ReturnForRevision -> resubmit"), never by silently appending to the pinned chain
        // in place.
        var returnResult = await harness.ReturnForRevisionAsync(voBId, Guid.NewGuid(), UserRole.ProjectDirector, "Policy changed mid-flight - resubmitting.");
        Assert.True(returnResult.IsSuccess);
        Assert.Equal(VariationOrderStatus.Draft, returnResult.Value.Status);
    }

    /// <summary>
    /// V-10 (domain-rules.md §7.4): a resubmission after ReturnForRevision re-resolves the chain from
    /// the (possibly changed) Amount, never carries the old chain forward. Exercises both directions -
    /// growing from 1 to 2 steps, then shrinking back to 1 - through the real
    /// Submit/ReturnForRevision/UpdateContent handlers against real persistence.
    /// </summary>
    [Fact]
    public async Task V10_Re_Pricing_During_A_Revision_Re_Resolves_The_Chain_Both_Growing_And_Shrinking()
    {
        var harness = new VariationOrderWorkflowHarness(now: Now);
        await harness.SeedDefaultApprovalPoliciesAsync(EffectiveFrom);
        var projectId = await harness.SeedProjectAsync(bac: 485_000_000.00m);
        var creatorId = Guid.NewGuid();
        var activityId = await harness.SeedActivityAsync(projectId, budgetCost: 5_000_000.00m);

        // Step 1: VO-022 Draft, A = +400,000.00, submitted -> chain [PM], TotalSteps = 1, rho = 1.
        var voId = await harness.CreateDraftAsync(
            projectId, 400_000.00m, creatorId, [new VariationOrderScopeItemInput(activityId, 400_000.00m)], voNumber: "VO-022");
        var firstSubmit = await harness.SubmitAsync(voId, creatorId, UserRole.QS);
        Assert.True(firstSubmit.IsSuccess);
        Assert.Equal(1, firstSubmit.Value.TotalSteps);
        Assert.Equal(1, firstSubmit.Value.RevisionNo);

        // Step 2: PM returns for revision -> Draft; rho = 2; chain snapshot rows deleted (count = 0).
        var returnResult = await harness.ReturnForRevisionAsync(voId, Guid.NewGuid(), UserRole.PM, "Needs re-pricing.");
        Assert.True(returnResult.IsSuccess);
        Assert.Equal(VariationOrderStatus.Draft, returnResult.Value.Status);
        Assert.Equal(2, returnResult.Value.RevisionNo);
        var afterReturn = await harness.LoadAsync(voId);
        Assert.Empty(afterReturn.ApprovalSteps);

        // Step 3: QS re-prices to A = +600,000.00 (scope payload updated so delta B_scope = 600,000.00)
        // and resubmits -> chain [PM, ProjectDirector], TotalSteps = 2, rho = 2, approvals restart at step 1.
        var updateResult = await harness.UpdateContentAsync(
            voId, 600_000.00m, 0, [new VariationOrderScopeItemInput(activityId, 600_000.00m)]);
        Assert.True(updateResult.IsSuccess, updateResult.IsFailure ? updateResult.Error : string.Empty);

        var secondSubmit = await harness.SubmitAsync(voId, creatorId, UserRole.QS);
        Assert.True(secondSubmit.IsSuccess);
        Assert.Equal(2, secondSubmit.Value.TotalSteps);
        Assert.Equal(2, secondSubmit.Value.RevisionNo);
        Assert.Equal([UserRole.PM, UserRole.ProjectDirector], secondSubmit.Value.ApprovalSteps.OrderBy(s => s.StepNo).Select(s => s.RequiredRole));

        // Step 4 (replay attempt): an actor holding the wrong role for the CURRENT (post-re-resolve)
        // chain is refused with 403-mapped NotAuthorizedForApprovalStep, never an unhandled exception.
        var wrongRoleAttempt = await harness.ApproveAsync(voId, Guid.NewGuid(), UserRole.Executive);
        Assert.True(wrongRoleAttempt.IsFailure);
        Assert.Equal("VariationOrderNotAuthorizedForApprovalStep", wrongRoleAttempt.Error);

        // Step 5 (reverse direction): re-price down to A = +300,000.00 -> chain SHRINKS to [PM].
        var returnAgain = await harness.ReturnForRevisionAsync(voId, Guid.NewGuid(), UserRole.PM, "Scope reduced.");
        Assert.True(returnAgain.IsSuccess);
        Assert.True((await harness.UpdateContentAsync(voId, 300_000.00m, 0, [new VariationOrderScopeItemInput(activityId, 300_000.00m)])).IsSuccess);
        var thirdSubmit = await harness.SubmitAsync(voId, creatorId, UserRole.QS);

        Assert.True(thirdSubmit.IsSuccess, thirdSubmit.IsFailure ? thirdSubmit.Error : string.Empty);
        Assert.Equal(1, thirdSubmit.Value.TotalSteps);
        Assert.Equal(UserRole.PM, thirdSubmit.Value.ApprovalSteps.Single().RequiredRole);
        Assert.Equal(3, thirdSubmit.Value.RevisionNo);
    }
}
