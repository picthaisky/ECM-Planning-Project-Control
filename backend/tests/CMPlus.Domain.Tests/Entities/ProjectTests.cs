using CMPlus.Domain.Common;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Domain.Tests.Entities;

public class ProjectTests
{
    private static Project CreateProject(decimal? retentionRate = null, decimal? advanceRate = null) =>
        Project.Create(
            tenantId: Guid.NewGuid(),
            name: "Test Project",
            code: "P-001",
            owner: "Owner Co.",
            contractStart: DateTimeOffset.Parse("2026-01-01T00:00:00+07:00"),
            contractFinish: DateTimeOffset.Parse("2026-12-31T00:00:00+07:00"),
            bac: 1_000_000m,
            dataDate: DateTimeOffset.Parse("2026-06-30T00:00:00+07:00"),
            retentionRate: retentionRate,
            advanceRate: advanceRate);

    [Fact]
    public void RetentionRate_Null_Is_Distinguishable_From_Zero()
    {
        var withNull = CreateProject(retentionRate: null);
        var withZero = CreateProject(retentionRate: 0m);

        Assert.Null(withNull.RetentionRate);
        Assert.NotNull(withZero.RetentionRate);
        Assert.Equal(0m, withZero.RetentionRate);
    }

    [Fact]
    public void AdvanceRate_Null_Is_Distinguishable_From_Zero()
    {
        var withNull = CreateProject(advanceRate: null);
        var withZero = CreateProject(advanceRate: 0m);

        Assert.Null(withNull.AdvanceRate);
        Assert.NotNull(withZero.AdvanceRate);
        Assert.Equal(0m, withZero.AdvanceRate);
    }

    [Theory]
    [InlineData(150, 100)]
    [InlineData(-10, 0)]
    [InlineData(42.5, 42.5)]
    public void RetentionRate_Clamps_To_0_100(decimal input, decimal expected)
    {
        var project = CreateProject(retentionRate: input);

        Assert.Equal(expected, project.RetentionRate);
    }

    [Theory]
    [InlineData(150, 100)]
    [InlineData(-10, 0)]
    [InlineData(42.5, 42.5)]
    public void AdvanceRate_Clamps_To_0_100(decimal input, decimal expected)
    {
        // Parity gap: RetentionRate had this theory, AdvanceRate (same PercentageGuard.Clamp
        // setter shape, Project.cs) did not - S4-QA-02 two-layer verification (US-4.4) needs both
        // percent fields checked at the domain defense-in-depth layer, not just one.
        var project = CreateProject(advanceRate: input);

        Assert.Equal(expected, project.AdvanceRate);
    }

    [Theory]
    [InlineData(150, 100)]
    [InlineData(-10, 0)]
    public void SetRetentionRate_Clamps_To_0_100_At_The_Setter(decimal input, decimal expected)
    {
        // S4-BE-02's UpdateProjectCommandHandler calls Project.SetRetentionRate (not the
        // constructor) on every edit - confirm the setter clamps identically to the constructor
        // path exercised above, since these are two different code paths in Project.cs.
        var project = CreateProject();

        project.SetRetentionRate(input);

        Assert.Equal(expected, project.RetentionRate);
    }

    [Theory]
    [InlineData(150, 100)]
    [InlineData(-10, 0)]
    public void SetAdvanceRate_Clamps_To_0_100_At_The_Setter(decimal input, decimal expected)
    {
        var project = CreateProject();

        project.SetAdvanceRate(input);

        Assert.Equal(expected, project.AdvanceRate);
    }

    [Fact]
    public void SetRetentionCapPercentage_Clamps_At_Setter()
    {
        var project = CreateProject();

        project.SetRetentionCapPercentage(250m);

        Assert.Equal(100m, project.RetentionCapPercentage);
    }

    [Fact]
    public void SetRetentionCapPercentage_Null_Means_Uncapped()
    {
        var project = CreateProject();

        project.SetRetentionCapPercentage(null);

        Assert.Null(project.RetentionCapPercentage);
    }

    [Fact]
    public void ContractValue_Defaults_To_Bac_When_Not_Specified()
    {
        var project = Project.Create(
            Guid.NewGuid(), "P", "C", "O",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMonths(6),
            bac: 500_000m, dataDate: DateTimeOffset.UtcNow);

        Assert.Equal(500_000m, project.ContractValue);
    }

    [Fact]
    public void ContractValue_Can_Be_Overridden()
    {
        var project = Project.Create(
            Guid.NewGuid(), "P", "C", "O",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMonths(6),
            bac: 500_000m, dataDate: DateTimeOffset.UtcNow, contractValue: 480_000m);

        Assert.Equal(480_000m, project.ContractValue);
    }

    // ---- ADR-0015: OriginalContractValue / EscalationBaselineContractValue ----

    [Fact]
    public void OriginalContractValue_Defaults_To_ContractValue_At_Construction_Bac_Default_Case()
    {
        var project = Project.Create(
            Guid.NewGuid(), "P", "C", "O",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMonths(6),
            bac: 500_000m, dataDate: DateTimeOffset.UtcNow); // contractValue not supplied -> defaults to bac

        Assert.Equal(500_000m, project.OriginalContractValue);
        Assert.Equal(project.ContractValue, project.OriginalContractValue);
    }

    [Fact]
    public void OriginalContractValue_Defaults_To_ContractValue_At_Construction_Explicit_Override_Case()
    {
        var project = Project.Create(
            Guid.NewGuid(), "P", "C", "O",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMonths(6),
            bac: 500_000m, dataDate: DateTimeOffset.UtcNow, contractValue: 480_000m);

        Assert.Equal(480_000m, project.OriginalContractValue);
    }

    [Fact]
    public void EscalationBaselineContractValue_Reads_OriginalContractValue_When_Present()
    {
        var project = CreateProject();

        Assert.Equal(project.OriginalContractValue, project.EscalationBaselineContractValue);
    }

    /// <summary>
    /// Sprint-10 security review H-02's explicitly requested invariant, the escalation-baseline
    /// counterpart to <c>Project.BAC == BAC(now)</c>: <c>EscalationBaselineContractValue</c> must
    /// equal <c>OriginalContractValue</c> unconditionally, in every state a <see cref="Project"/> can
    /// reach - never a fallback to <c>ContractValue</c> (the exact self-diluting-denominator shape
    /// ADR-0015 exists to remove). Before the fix, this held only when <c>OriginalContractValue</c>
    /// happened to be non-null; a NULL "legacy" row would have silently broken it. Now that the column
    /// is NOT NULL and the property is a trivial passthrough, this holds by construction rather than
    /// by convention - asserted across every mutator that touches either field, so the invariant
    /// cannot silently reopen if a future change reintroduces a fallback.
    /// </summary>
    [Fact]
    public void Invariant_EscalationBaselineContractValue_Always_Equals_OriginalContractValue()
    {
        var project = CreateProject();
        Assert.Equal(project.OriginalContractValue, project.EscalationBaselineContractValue); // fresh project

        project.SetContractValue(project.ContractValue + 50_000_000m);
        Assert.Equal(project.OriginalContractValue, project.EscalationBaselineContractValue); // after an ordinary edit

        // A BAC edit is only legal before any VO is approved (ADR-0017), so exercise it first.
        project.SetBac(project.BAC + 1_000_000m);
        Assert.Equal(project.OriginalContractValue, project.EscalationBaselineContractValue); // after a BAC edit

        project.ApplyVariationOrderApproval(2_000_000.00m, DateTimeOffset.UtcNow);
        Assert.Equal(project.OriginalContractValue, project.EscalationBaselineContractValue); // after a VO approval
    }

    [Fact]
    public void SetContractValue_Moves_ContractValue_But_Never_OriginalContractValue()
    {
        // ADR-0015: OriginalContractValue moves only via a formal contract amendment (not yet built -
        // domain-rules.md §4.4). Plain Project Info master-data edits (SetContractValue, the same
        // setter UpdateProjectCommandHandler calls) are NOT a contract amendment and must never
        // silently rebase the escalation baseline - that would let an ordinary data edit erase VO
        // escalation history exactly the way ADR-0015 condemns a cheap-amendment reset for doing.
        var project = CreateProject(); // OriginalContractValue == ContractValue == BAC initially
        var originalBaseline = project.OriginalContractValue;

        project.SetContractValue(project.ContractValue + 50_000_000m);

        Assert.Equal(originalBaseline, project.OriginalContractValue);
        Assert.NotEqual(project.ContractValue, project.OriginalContractValue);
        // EscalationBaselineContractValue must track the untouched baseline, not the edited current
        // value - this is the exact self-dilution shape ADR-0015 fixes, asserted directly on Project.
        Assert.Equal(originalBaseline, project.EscalationBaselineContractValue);
    }

    // ---- domain-rules.md §5.5(c): OriginalBac / EffectiveOriginalBac ----

    [Fact]
    public void OriginalBac_Defaults_To_Bac_At_Construction()
    {
        var project = Project.Create(
            Guid.NewGuid(), "P", "C", "O",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMonths(6),
            bac: 500_000m, dataDate: DateTimeOffset.UtcNow);

        Assert.Equal(500_000m, project.OriginalBac);
        Assert.Equal(project.BAC, project.OriginalBac);
    }

    [Fact]
    public void EffectiveOriginalBac_Reads_OriginalBac_When_Present()
    {
        var project = CreateProject();

        Assert.Equal(project.OriginalBac, project.EffectiveOriginalBac);
    }

    /// <summary>
    /// ADR-0017. This test previously asserted that <c>SetBac</c> moves <c>BAC</c> but never
    /// <c>OriginalBac</c> — which was S10-SEC-02 finding M-02 itself, encoded as a guarantee: the
    /// edit persisted and displayed while every EVM figure (derived from
    /// <c>OriginalBac + ΣVO</c>) silently ignored it. The human ruled 2026-08-10 that the edit is
    /// legal only before any VO is approved, and moves both figures together so the §5.5(c)
    /// invariant holds.
    /// </summary>
    [Fact]
    public void SetBac_Before_Any_Vo_Moves_Bac_And_Its_Baseline_Together_Keeping_The_Invariant()
    {
        var project = CreateProject(); // OriginalBac == BAC, no approved VO yet

        project.SetBac(project.BAC + 50_000_000m);

        Assert.Equal(project.BAC, project.OriginalBac);
        Assert.Equal(project.BAC, project.EffectiveOriginalBac);
    }

    [Fact]
    public void SetBac_Is_Refused_Once_A_Vo_Has_Been_Approved_Rather_Than_Being_Silently_Ignored()
    {
        var project = CreateProject();
        project.ApplyVariationOrderApproval(2_000_000.00m, DateTimeOffset.UtcNow);
        var bacBefore = project.BAC;
        var baselineBefore = project.OriginalBac;

        var exception = Assert.Throws<DomainException>(() => project.SetBac(project.BAC + 1_000_000m));

        // The message must explain *why*, not merely refuse - a PM who hits this needs to know the
        // budget is derived, and what to do instead.
        Assert.Contains("Variation Order", exception.Message);
        Assert.Equal(bacBefore, project.BAC);
        Assert.Equal(baselineBefore, project.OriginalBac);
    }

    /// <summary>
    /// The offsetting-VO case is why <c>ApprovedVariationOrderCount</c> is counted explicitly rather
    /// than inferred from <c>OriginalBac != BAC</c>: a Deduct VO that exactly cancels an earlier Add
    /// leaves those two equal while approved VOs genuinely exist, and the inference would quietly
    /// permit the edit this rule blocks.
    /// </summary>
    [Fact]
    public void SetBac_Is_Still_Refused_When_Offsetting_Vos_Leave_Bac_Equal_To_Its_Baseline()
    {
        var project = CreateProject();
        project.ApplyVariationOrderApproval(1_000_000.00m, DateTimeOffset.UtcNow);
        project.ApplyVariationOrderApproval(-1_000_000.00m, DateTimeOffset.UtcNow);

        Assert.Equal(project.OriginalBac, project.BAC); // the inference would say "no VOs here"
        Assert.Equal(2, project.ApprovedVariationOrderCount);
        Assert.Throws<DomainException>(() => project.SetBac(project.BAC + 1_000_000m));
    }

    [Fact]
    public void ApplyVariationOrderApproval_Never_Moves_OriginalBac()
    {
        // domain-rules.md §5.5(c): BAC^orig moves only with a future formal rebaseline event (never
        // contemplated by this sprint), never as a side effect of an ordinary VO approval - otherwise
        // BAC(t) reconstruction for dates before this VO's own ApprovedAt would retroactively change
        // the instant the VO is approved, exactly the defect this field exists to prevent.
        var project = CreateProject();
        var originalBaseline = project.OriginalBac;

        project.ApplyVariationOrderApproval(5_000_000.00m, DateTimeOffset.UtcNow);

        Assert.Equal(originalBaseline, project.OriginalBac);
    }

    [Fact]
    public void EacVariantDefault_Defaults_To_CpiBased()
    {
        var project = CreateProject();

        Assert.Equal(EacVariant.CpiBased, project.EacVariantDefault);
    }

    [Fact]
    public void SetEacVariantDefault_Changes_The_Variant()
    {
        var project = CreateProject();

        project.SetEacVariantDefault(EacVariant.CpiSpiBased);

        Assert.Equal(EacVariant.CpiSpiBased, project.EacVariantDefault);
    }

    [Fact]
    public void SetEacCustomPerformanceFactor_Rejects_Zero_Or_Negative()
    {
        var project = CreateProject();

        Assert.Throws<DomainException>(() => project.SetEacCustomPerformanceFactor(0m));
        Assert.Throws<DomainException>(() => project.SetEacCustomPerformanceFactor(-1.5m));
    }

    [Fact]
    public void SetEacCustomPerformanceFactor_Accepts_Positive_Value()
    {
        var project = CreateProject();

        project.SetEacCustomPerformanceFactor(1.25m);

        Assert.Equal(1.25m, project.EacCustomPerformanceFactor);
    }

    [Fact]
    public void SetEacManualEtc_Rejects_Negative()
    {
        var project = CreateProject();

        Assert.Throws<DomainException>(() => project.SetEacManualEtc(-1m));
    }

    [Fact]
    public void SetEacManualEtc_Accepts_Zero_Or_Positive()
    {
        var project = CreateProject();

        project.SetEacManualEtc(0m);
        Assert.Equal(0m, project.EacManualEtc);

        project.SetEacManualEtc(760_000.00m);
        Assert.Equal(760_000.00m, project.EacManualEtc);
    }

    // ---- S10-BE-03: ApplyVariationOrderApproval (domain-rules.md §5.1) ----

    [Fact]
    public void ApplyVariationOrderApproval_Moves_Bac_And_ContractValue_By_The_Same_Signed_Amount_Add()
    {
        var project = CreateProject(); // BAC == ContractValue == 1,000,000.00

        project.ApplyVariationOrderApproval(2_400_000.00m, DateTimeOffset.Parse("2026-08-10T09:00:00+07:00"));

        Assert.Equal(3_400_000.00m, project.BAC);
        Assert.Equal(3_400_000.00m, project.ContractValue);
    }

    /// <summary>
    /// The exact risk domain-rules.md §3.4 flags for R2 extended to the approval effect: a Deduct
    /// VO's signed <c>Amount</c> must drive BOTH figures DOWN. An implementation that silently applies
    /// <c>Math.Abs(amount)</c> anywhere on this path would make BAC go UP on a Deduct VO instead - this
    /// test fails loudly if that mutation is ever introduced.
    /// </summary>
    [Fact]
    public void ApplyVariationOrderApproval_Moves_Bac_And_ContractValue_Down_For_A_Deduct_Signed_Negative_Amount()
    {
        var project = CreateProject(); // BAC == ContractValue == 1,000,000.00

        project.ApplyVariationOrderApproval(-300_000.00m, DateTimeOffset.Parse("2026-08-10T09:00:00+07:00"));

        Assert.Equal(700_000.00m, project.BAC);
        Assert.Equal(700_000.00m, project.ContractValue);
    }

    [Fact]
    public void ApplyVariationOrderApproval_Never_Moves_OriginalContractValue()
    {
        // ADR-0015/domain-rules.md §4.4: the baseline moves only via a formal ContractAmendment
        // (not yet built) - never as a side effect of an ordinary VO approval.
        var project = CreateProject();
        var originalBaseline = project.OriginalContractValue;

        project.ApplyVariationOrderApproval(5_000_000.00m, DateTimeOffset.UtcNow);

        Assert.Equal(originalBaseline, project.OriginalContractValue);
    }

    [Fact]
    public void ApplyVariationOrderApproval_Throws_Rather_Than_Clamps_When_A_Deduct_Would_Drive_Bac_Negative()
    {
        // domain-rules.md §5.1's Guard: the handler is expected to pre-validate this and never reach
        // here on a well-formed request (422 VoWouldMakeContractValueNegative) - this proves the
        // Domain-level guard is real defense-in-depth, not merely relied upon.
        var project = CreateProject(); // BAC == 1,000,000.00

        Assert.Throws<DomainException>(() => project.ApplyVariationOrderApproval(-1_000_000.01m, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void ApplyVariationOrderApproval_Stamps_EacManualEtcStaleSince_When_A_Manual_Etc_Is_Set()
    {
        // domain-rules.md §5.7: a stale manual ETC would otherwise let a fully-funded VO make VAC
        // improve by the full VO amount while EAC does not move at all - "a project forecast over
        // budget now reports exactly on budget" purely because the estimate went stale.
        var project = CreateProject();
        project.SetEacManualEtc(70_000_000.00m);
        var approvedAt = DateTimeOffset.Parse("2026-08-10T09:00:00+07:00");

        project.ApplyVariationOrderApproval(10_000_000.00m, approvedAt);

        Assert.Equal(approvedAt, project.EacManualEtcStaleSince);
        // And the manual figure itself is NEVER auto-adjusted arithmetically - a bottom-up estimate
        // is a professional judgement, not an arithmetic series (domain-rules.md §5.7's explicit rule).
        Assert.Equal(70_000_000.00m, project.EacManualEtc);
    }

    [Fact]
    public void ApplyVariationOrderApproval_Does_Not_Stamp_EacManualEtcStaleSince_When_No_Manual_Etc_Is_Set()
    {
        var project = CreateProject(); // EacManualEtc is null by default

        project.ApplyVariationOrderApproval(1_000_000.00m, DateTimeOffset.UtcNow);

        Assert.Null(project.EacManualEtcStaleSince);
    }

    [Fact]
    public void ApplyVariationOrderApproval_Does_Not_Move_EacManualEtcStaleSince_Once_Already_Stamped_By_An_Earlier_Vo()
    {
        // Two VOs approved while a manual ETC is stale: the staleness marker records WHEN it first
        // went stale, not the most recent VO - re-stamping on every subsequent VO would not change
        // correctness here, but the first-stale timestamp is the more useful audit signal, and this
        // pins the actual (first-write-wins) behaviour so a future change is a deliberate decision.
        var project = CreateProject();
        project.SetEacManualEtc(50_000_000.00m);
        var firstApproval = DateTimeOffset.Parse("2026-08-10T09:00:00+07:00");
        var secondApproval = DateTimeOffset.Parse("2026-08-11T09:00:00+07:00");

        project.ApplyVariationOrderApproval(1_000_000.00m, firstApproval);
        project.ApplyVariationOrderApproval(2_000_000.00m, secondApproval);

        Assert.Equal(firstApproval, project.EacManualEtcStaleSince);
    }

    [Fact]
    public void SetEacManualEtc_Clears_EacManualEtcStaleSince()
    {
        // domain-rules.md §5.7: "The value is cleared when a QS re-enters EacManualEtc" - the only
        // legitimate way the staleness marker is ever cleared.
        var project = CreateProject();
        project.SetEacManualEtc(50_000_000.00m);
        project.ApplyVariationOrderApproval(1_000_000.00m, DateTimeOffset.UtcNow);
        Assert.NotNull(project.EacManualEtcStaleSince);

        project.SetEacManualEtc(52_000_000.00m);

        Assert.Null(project.EacManualEtcStaleSince);
    }

    [Fact]
    public void Constructor_Rejects_Negative_Bac()
    {
        Assert.Throws<DomainException>(() => Project.Create(
            Guid.NewGuid(), "P", "C", "O",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMonths(1),
            bac: -1m, dataDate: DateTimeOffset.UtcNow));
    }

    [Fact]
    public void SetDefectsLiabilityMonths_Rejects_Negative()
    {
        var project = CreateProject();

        Assert.Throws<DomainException>(() => project.SetDefectsLiabilityMonths(-1));
    }

    [Fact]
    public void SetAdvanceAmountPaid_Rejects_Negative()
    {
        var project = CreateProject();

        Assert.Throws<DomainException>(() => project.SetAdvanceAmountPaid(-100m));
    }

    [Fact]
    public void RetentionRelease1Percentage_Defaults_To_50()
    {
        var project = CreateProject();

        Assert.Equal(50.00m, project.RetentionRelease1Percentage);
    }

    [Fact]
    public void AdvanceRecoveryMethod_Defaults_To_ProRata()
    {
        var project = CreateProject();

        Assert.Equal(AdvanceRecoveryMethod.ProRata, project.AdvanceRecoveryMethod);
    }

    [Fact]
    public void SetAdvanceRecoveryMethod_ThresholdBanded_Stores_Banded_Params_Clamped()
    {
        var project = CreateProject();

        project.SetAdvanceRecoveryMethod(AdvanceRecoveryMethod.ThresholdBanded, startPct: 10m, ratePct: 125m, endPct: 90m);

        Assert.Equal(AdvanceRecoveryMethod.ThresholdBanded, project.AdvanceRecoveryMethod);
        Assert.Equal(10m, project.AdvanceRecoveryStartPct);
        Assert.Equal(100m, project.AdvanceRecoveryRatePct); // clamped from 125
        Assert.Equal(90m, project.AdvanceRecoveryEndPct);
    }

    [Fact]
    public void Constructor_Rejects_Blank_Required_Fields()
    {
        Assert.Throws<DomainException>(() => Project.Create(
            Guid.NewGuid(), "  ", "C", "O",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMonths(1),
            bac: 1m, dataDate: DateTimeOffset.UtcNow));
    }

    // ------------------------------------------------------------------------------------
    // S4-BE-02 (US-4.3/4.4): UpdateProjectCommand's domain-level defense-in-depth setters.
    // ------------------------------------------------------------------------------------

    [Fact]
    public void SetContractDates_Rejects_Finish_Before_Start()
    {
        var project = CreateProject();

        Assert.Throws<DomainException>(() => project.SetContractDates(
            DateTimeOffset.Parse("2026-06-01T00:00:00Z"), DateTimeOffset.Parse("2026-05-01T00:00:00Z")));
    }

    [Fact]
    public void SetContractDates_Accepts_Finish_Equal_To_Start()
    {
        var project = CreateProject();
        var same = DateTimeOffset.Parse("2026-06-01T00:00:00Z");

        project.SetContractDates(same, same);

        Assert.Equal(same, project.ContractStart);
        Assert.Equal(same, project.ContractFinish);
    }

    [Fact]
    public void SetContractDates_Accepts_Finish_After_Start()
    {
        var project = CreateProject();
        var start = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var finish = DateTimeOffset.Parse("2026-12-31T00:00:00Z");

        project.SetContractDates(start, finish);

        Assert.Equal(start, project.ContractStart);
        Assert.Equal(finish, project.ContractFinish);
    }

    [Fact]
    public void SetCode_Rejects_Blank()
    {
        var project = CreateProject();

        Assert.Throws<DomainException>(() => project.SetCode("  "));
    }

    [Fact]
    public void SetCode_Trims_And_Assigns()
    {
        var project = CreateProject();

        project.SetCode(" P-002 ");

        Assert.Equal("P-002", project.Code);
    }

    [Fact]
    public void SetOwner_Rejects_Blank()
    {
        var project = CreateProject();

        Assert.Throws<DomainException>(() => project.SetOwner(""));
    }

    [Fact]
    public void SetOwner_Trims_And_Assigns()
    {
        var project = CreateProject();

        project.SetOwner(" New Owner Co. ");

        Assert.Equal("New Owner Co.", project.Owner);
    }

    [Fact]
    public void SetBac_Rejects_Negative()
    {
        var project = CreateProject();

        Assert.Throws<DomainException>(() => project.SetBac(-1m));
    }

    [Fact]
    public void SetBac_Accepts_Zero_Or_Positive()
    {
        var project = CreateProject();

        project.SetBac(2_500_000.50m);

        Assert.Equal(2_500_000.50m, project.BAC);
    }
}
