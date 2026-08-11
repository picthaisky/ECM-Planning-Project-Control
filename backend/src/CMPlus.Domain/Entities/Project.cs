using CMPlus.Domain.Common;
using CMPlus.Domain.Enums;

namespace CMPlus.Domain.Entities;

/// <summary>
/// A construction project owned by a tenant (docs/9 §4, extended by docs/specs/master-plan
/// reconciliation - ADR-0007 EAC fields, domain-decisions.md §2.6 retention/advance fields, all
/// landing in the Sprint 1 migration per docs/10 §3).
/// </summary>
public sealed class Project : Entity, ITenantOwned
{
    public Guid TenantId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string Code { get; private set; } = string.Empty;

    public string Owner { get; private set; } = string.Empty;

    public DateTimeOffset ContractStart { get; private set; }

    public DateTimeOffset ContractFinish { get; private set; }

    public decimal BAC { get; private set; }

    /// <summary>
    /// domain-rules.md §5.5(c)/§1 ($BAC^{orig}$): the EVM budget baseline as first set, before any VO -
    /// needed to make $BAC(t)$ reconstructible ($BAC(t) = BAC^{orig} + \sum$ approved VO amounts with
    /// $ApprovedAt \le t$) rather than a live scalar read that silently rewrites every historical query
    /// the instant a VO is approved. Same shape/rationale as <see cref="OriginalContractValue"/> -
    /// deliberately NOT a settable constructor parameter (there is no legitimate way for a caller to
    /// construct a Project whose baseline differs from its starting BAC) and moved only by
    /// <see cref="ApplyVariationOrderApproval"/>'s deliberate non-movement (i.e. never) - a future
    /// formal rebaseline event, symmetric with <see cref="OriginalContractValue"/>'s
    /// <c>ContractAmendment</c> hook, is the only thing domain-rules.md contemplates ever moving it.
    /// <b>NOT NULL</b> (sprint-10 security review H-02): the migration that adds this column backfills
    /// every pre-existing row (<c>OriginalBac = BAC</c>, safe because no <see cref="VariationOrder"/>
    /// can exist before this migration runs) and then tightens the column to NOT NULL. A nullable
    /// baseline that silently degraded to the live <see cref="BAC"/> for "legacy" rows was found to
    /// reinstate the exact self-diluting-denominator defect ADR-0015 exists to remove, let an ordinary
    /// (non-Admin-only) <c>PUT /api/v1/projects/{id}</c> move the escalation trigger, and made
    /// <c>GetBacAsOfAsync</c> double-count every approved VO in historical EVM reads - three
    /// independent, execution-verified consequences of one missing backfill. A misconfigured row is
    /// now a build/migration-time impossibility instead of a silent, hard-to-detect data problem.
    /// </summary>
    public decimal OriginalBac { get; private set; }

    /// <summary>
    /// How many Variation Orders have been approved against this project. Counted explicitly rather
    /// than inferred from <c>OriginalBac != BAC</c>, which looks equivalent but is not: a Deduct VO
    /// that exactly offsets an earlier Add leaves the two equal while approved VOs genuinely exist,
    /// and the inference would then quietly permit the edit ADR-0017 blocks.
    /// </summary>
    public int ApprovedVariationOrderCount { get; private set; }

    /// <summary>
    /// The value every $BAC(t)$ reconstruction must read. Sprint-10 security review H-02: this used to
    /// fall back to the live <see cref="BAC"/> for a "legacy" row whose <see cref="OriginalBac"/> was
    /// NULL; <see cref="OriginalBac"/> is now NOT NULL (backfilled by migration), so this is a trivial,
    /// always-safe passthrough - kept as a named property (rather than deleted) purely so call sites
    /// that read "the $BAC(t)$ reconstruction origin" do not need to know or care that it happens to be
    /// a plain column today.
    /// </summary>
    public decimal EffectiveOriginalBac => OriginalBac;

    /// <summary>
    /// Nullable so "not yet configured" is distinguishable from a deliberate 0% (S1-BE-01 DoD) -
    /// never defaulted to 0 silently.
    /// </summary>
    public decimal? RetentionRate { get; private set; }

    /// <summary>Nullable for the same reason as <see cref="RetentionRate"/>: null != 0.</summary>
    public decimal? AdvanceRate { get; private set; }

    public DateTimeOffset DataDate { get; private set; }

    // --- EAC (ADR-0007) ---

    /// <summary>Not-null, defaults to <see cref="EacVariant.CpiBased"/> per ADR-0007/docs/10 §3.</summary>
    public EacVariant EacVariantDefault { get; private set; } = EacVariant.CpiBased;

    /// <summary><c>decimal(9,4)</c>, must be &gt; 0 when supplied (ADR-0007).</summary>
    public decimal? EacCustomPerformanceFactor { get; private set; }

    /// <summary><c>decimal(18,2)</c>, must be &gt;= 0 when supplied (ADR-0007).</summary>
    public decimal? EacManualEtc { get; private set; }

    /// <summary>
    /// S10-BE-03 (domain-rules.md §5.7): stamped to the VO's <c>ApprovedAt</c> whenever an approved
    /// Variation Order moves <see cref="BAC"/> while <see cref="EacManualEtc"/> is set. A manual
    /// (bottom-up) ETC does not know about the VO, so <c>BottomUpEtc</c>'s $EAC$ would not move while
    /// $VAC$ improves by the full VO amount - "a project forecast over budget now reports exactly on
    /// budget" purely because the estimate went stale, not because anything real improved. Never
    /// cleared automatically - only by a QS re-entering <see cref="EacManualEtc"/>
    /// (<see cref="SetEacManualEtc"/>) - and never used to auto-adjust the manual figure arithmetically
    /// (a bottom-up estimate is a professional judgement, not an arithmetic series).
    /// </summary>
    public DateTimeOffset? EacManualEtcStaleSince { get; private set; }

    // --- Retention / advance (domain-decisions.md §2, docs/10 §3) ---

    /// <summary>Defaults to <see cref="BAC"/> at construction; retention/advance accrue against
    /// contract value, not budget (domain-decisions.md §2.2). <b>This is the CURRENT contract sum
    /// (includes approved VOs) - ADR-0015: it must never be used as the cumulative-VO-escalation
    /// denominator</b> (that field is <see cref="OriginalContractValue"/> /
    /// <see cref="EscalationBaselineContractValue"/> below). It remains exactly what it always was:
    /// the retention-cap and advance-rate base.</summary>
    public decimal ContractValue { get; private set; }

    /// <summary>
    /// ADR-0015 / domain-rules.md §4.3-§4.4: the baseline contract value the cumulative-VO-escalation
    /// ratio is measured against - the Accepted Contract Amount as first signed, moved only by a
    /// formal contract amendment (<c>ContractAmendment</c>, not yet built - domain-rules.md §4.4 marks
    /// it as the future hook; this field does not depend on that entity existing). <b>Always equals
    /// <see cref="ContractValue"/> at construction</b> (project creation, before any VO can exist) and
    /// is deliberately a separate, independently-settable field rather than derived, so a future
    /// amendment can move it without touching <see cref="ContractValue"/>'s own update path.
    /// <para><b>NOT NULL</b> (sprint-10 security review H-02): the migration that adds this column
    /// backfills every pre-existing row (<c>OriginalContractValue = ContractValue</c>, safe because no
    /// <see cref="VariationOrder"/> can exist before this migration runs, so no approved VO could ever
    /// have moved <see cref="ContractValue"/> away from its original figure) and then tightens the
    /// column to NOT NULL. Three independent, execution-verified consequences followed from leaving
    /// this nullable-with-a-fallback: the self-diluting denominator ADR-0015 exists to remove was
    /// silently reinstated; an ordinary <c>PUT /api/v1/projects/{id}</c> (not Admin-only) moved the
    /// escalation trigger and could drop the required <c>Executive</c> step; and <c>GetBacAsOfAsync</c>
    /// double-counted every approved VO in historical EVM reads. A nullable baseline that silently
    /// degrades to the live figure is worse than a NOT NULL column with a loud migration-time error -
    /// it turns a provisioning gap into a silently weaker control instead of a visible one.</para>
    /// </summary>
    public decimal OriginalContractValue { get; private set; }

    /// <summary>
    /// ADR-0015: the single value every escalation-ratio consumer (<c>ApprovalRoutingService</c>,
    /// and Sprint 10's VO submit path) must read. Sprint-10 security review H-02:
    /// <see cref="OriginalContractValue"/> is now NOT NULL (backfilled by migration), so this is a
    /// trivial, always-safe passthrough - never <see cref="ContractValue"/> directly, which is what
    /// made the shipped <c>ApprovalRoutingService.cs:66-70</c> self-diluting (every approved VO raised
    /// the denominator, so the escalation trigger receded exactly as the contract drifted furthest
    /// from its original scope). Kept as a named property (rather than deleted) purely so call sites
    /// do not need to know or care that the baseline happens to be a plain column today.
    /// </summary>
    public decimal EscalationBaselineContractValue => OriginalContractValue;

    /// <summary>Null = uncapped (Thai-standard contracts); a value = FIDIC-style hard cap.</summary>
    public decimal? RetentionCapPercentage { get; private set; }

    public decimal RetentionRelease1Percentage { get; private set; } = 50.00m;

    public int? DefectsLiabilityMonths { get; private set; }

    public decimal? AdvanceAmountPaid { get; private set; }

    public AdvanceRecoveryMethod AdvanceRecoveryMethod { get; private set; } = AdvanceRecoveryMethod.ProRata;

    /// <summary>Only meaningful when <see cref="AdvanceRecoveryMethod"/> is
    /// <see cref="AdvanceRecoveryMethod.ThresholdBanded"/> (domain-decisions.md §2.3).</summary>
    public decimal? AdvanceRecoveryStartPct { get; private set; }

    public decimal? AdvanceRecoveryRatePct { get; private set; }

    public decimal? AdvanceRecoveryEndPct { get; private set; }

    /// <summary>
    /// Sprint-10 security review H-03: SQL Server <c>rowversion</c> optimistic-concurrency token,
    /// identical role/shape to <see cref="VariationOrder.RowVersion"/>/<see cref="PaymentCertificate.RowVersion"/>.
    /// Before this field existed, <see cref="Project"/> was the one money-moving aggregate with no
    /// concurrency protection at all: two concurrent <see cref="VariationOrder"/> final approvals on
    /// the SAME project (two different VO rows, so neither VO's own <c>RowVersion</c> conflicts) could
    /// both read-modify-write <see cref="BAC"/>/<see cref="ContractValue"/> with the second silently
    /// discarding the first's money move - execution-verified: two approved VOs whose stamped
    /// <c>BacBefore</c>/<c>BacAfter</c> figures agreed with neither the pre- nor the post-race
    /// <see cref="BAC"/>. <c>IVariationOrderRepository.TrySaveChangesAsync</c>/
    /// <c>IPaymentCertificateRepository.TrySaveChangesAsync</c> already translate any
    /// <c>DbUpdateConcurrencyException</c> on their scoped <c>DbContext</c> - which now includes a
    /// conflicting <see cref="Project"/> row, not only the document's own row - into a 409 for the
    /// loser, who retries and has the escalation-bypass guard (domain-rules.md §4.7) re-read a
    /// consistent $\Sigma^{VO}$ rather than a stale one.
    /// </summary>
    public byte[] RowVersion { get; private set; } = [];

    // EF Core materialization fallback: empirically, EF's ConstructorBindingConvention can refuse
    // to bind an all-caps property name like BAC to a same-named constructor parameter even
    // though ordinary PascalCase properties (Name, Code, Owner, ...) bind correctly - and when the
    // "rich" constructor fails to fully bind, EF does NOT fall back gracefully unless another,
    // fully-bindable constructor (trivially, the parameterless one) also exists; without it, model
    // building throws "No suitable constructor was found". Every entity in this project keeps a
    // private parameterless constructor for this reason, not because it is otherwise needed.
    private Project()
    {
    }

    public Project(
        Guid tenantId,
        string name,
        string code,
        string owner,
        DateTimeOffset contractStart,
        DateTimeOffset contractFinish,
        decimal bac,
        decimal contractValue,
        DateTimeOffset dataDate,
        decimal? retentionRate,
        decimal? advanceRate)
    {
        TenantId = tenantId;
        Name = ValidateRequired(name, nameof(Name));
        Code = ValidateRequired(code, nameof(Code));
        Owner = ValidateRequired(owner, nameof(Owner));
        ContractStart = contractStart;
        ContractFinish = contractFinish;
        BAC = MoneyGuard.EnsureNonNegative(bac, nameof(BAC));
        // domain-rules.md §5.5(c): BAC^orig - no VO can exist yet at construction time, so the current
        // and original budgets are, by definition, identical the moment a project is created. Same
        // "not a settable constructor parameter" reasoning as OriginalContractValue below.
        OriginalBac = BAC;
        ContractValue = MoneyGuard.EnsureNonNegative(contractValue, nameof(ContractValue));
        // ADR-0015 / domain-rules.md §4.3: "default it to ContractValue at project creation" - no VO
        // can exist yet at construction time, so the current and original contract sums are, by
        // definition, identical the moment a project is created. Deliberately NOT a settable
        // constructor parameter: there is no legitimate way for a caller to construct a Project whose
        // baseline differs from its starting contract value, so this is not a choice to expose.
        OriginalContractValue = ContractValue;
        DataDate = dataDate;
        RetentionRate = PercentageGuard.Clamp(retentionRate);
        AdvanceRate = PercentageGuard.Clamp(advanceRate);
    }

    /// <summary>
    /// Convenience factory: defaults <see cref="ContractValue"/> to <paramref name="bac"/> when
    /// not supplied (docs/10 §3: "Project.ContractValue decimal(18,2) (default = BAC)"). Kept as a
    /// static method rather than a constructor overload/default parameter so the entity still has
    /// exactly one constructor for EF Core materialization to bind unambiguously.
    /// </summary>
    public static Project Create(
        Guid tenantId,
        string name,
        string code,
        string owner,
        DateTimeOffset contractStart,
        DateTimeOffset contractFinish,
        decimal bac,
        DateTimeOffset dataDate,
        decimal? retentionRate = null,
        decimal? advanceRate = null,
        decimal? contractValue = null) =>
        new(tenantId, name, code, owner, contractStart, contractFinish, bac,
            contractValue ?? bac, dataDate, retentionRate, advanceRate);

    public void Rename(string name) => Name = ValidateRequired(name, nameof(Name));

    public void SetCode(string code) => Code = ValidateRequired(code, nameof(Code));

    public void SetOwner(string owner) => Owner = ValidateRequired(owner, nameof(Owner));

    /// <summary>
    /// S4-BE-02 (US-4.3): rejects a finish earlier than start as a domain invariant - defense in
    /// depth alongside <c>UpdateProjectCommandValidator</c>'s client-visible FluentValidation rule
    /// ("no silent acceptance of an invalid date range").
    /// </summary>
    public void SetContractDates(DateTimeOffset contractStart, DateTimeOffset contractFinish)
    {
        if (contractFinish < contractStart)
        {
            throw new DomainException("ContractFinish cannot be earlier than ContractStart.");
        }

        ContractStart = contractStart;
        ContractFinish = contractFinish;
    }

    /// <summary>
    /// Corrects the budget at completion, and moves <see cref="OriginalBac"/> with it so the
    /// §5.5(c) invariant <c>Project.BAC == BAC(now)</c> keeps holding. Legal only while **no**
    /// Variation Order has been approved, at which point $\Sigma^{VO} = 0$ and the two figures are
    /// necessarily equal anyway — i.e. this is the ordinary "fix the budget during setup" case.
    ///
    /// <para><b>Blocked once an approved VO exists</b> (ADR-0017, human-confirmed 2026-08-10, closing
    /// S10-SEC-02 finding M-02). Sprint 10 repointed every EVM read at
    /// $BAC(t) = OriginalBac + \Sigma(\text{approved VOs} \le t)$, which left a plain assignment here
    /// persisting and displaying on the Project Info screen while the dashboard, EAC, VAC, TCPI and
    /// % complete all silently ignored it. Both obvious repairs are worse than refusing: assigning
    /// <see cref="BAC"/> alone is the silent no-op just described, and rebasing
    /// <see cref="OriginalBac"/> retroactively rewrites every historical EVM figure — diverging live
    /// queries from the immutable <c>EvmPeriodSnapshot</c> rows closed against them (ADR-0009). A
    /// loud refusal has no quiet failure mode in either direction.</para>
    ///
    /// <para>The sibling <see cref="SetContractValue"/> deliberately does <b>not</b> get the same
    /// treatment: the cumulative-VO escalation denominator *is* the baseline, so moving it on an
    /// ordinary edit would reinstate the bypass S10-SEC-01 H-02 just closed. The two look symmetric
    /// and are not — one affects EVM history, the other affects approval authority.</para>
    ///
    /// <para>A deliberate <i>rebaseline</i> (re-cutting the baseline so prior VOs fold into it, with
    /// its own authority and audit record) is the intended follow-up and is not this method — see
    /// ADR-0017's consequences.</para>
    /// </summary>
    public void SetBac(decimal bac)
    {
        // A no-op is always allowed: `UpdateProjectCommand` is a full-representation PUT, so an
        // unchanged BAC arrives on every edit of any other field and must not be rejected.
        if (bac == BAC)
        {
            return;
        }

        if (ApprovedVariationOrderCount > 0)
        {
            throw new DomainException(
                $"BAC cannot be edited directly once a Variation Order has been approved " +
                $"({ApprovedVariationOrderCount} approved). The budget at completion is derived as " +
                "OriginalBac plus approved variations, so a direct edit would either be ignored by " +
                "every EVM figure or retroactively rewrite published history. Raise a Variation " +
                "Order, or rebaseline the project.");
        }

        BAC = MoneyGuard.EnsureNonNegative(bac, nameof(BAC));
        OriginalBac = BAC;
    }

    public void SetDataDate(DateTimeOffset dataDate) => DataDate = dataDate;

    public void SetContractValue(decimal contractValue) =>
        ContractValue = MoneyGuard.EnsureNonNegative(contractValue, nameof(ContractValue));

    public void SetRetentionRate(decimal? retentionRate) => RetentionRate = PercentageGuard.Clamp(retentionRate);

    public void SetAdvanceRate(decimal? advanceRate) => AdvanceRate = PercentageGuard.Clamp(advanceRate);

    /// <summary>Restricted to PM/QS/Executive and audited at the Application layer (ADR-0007(c));
    /// the Domain method itself only enforces the assignment, not the permission check.</summary>
    public void SetEacVariantDefault(EacVariant variant) => EacVariantDefault = variant;

    public void SetEacCustomPerformanceFactor(decimal? performanceFactor)
    {
        if (performanceFactor is <= 0)
        {
            throw new DomainException("EacCustomPerformanceFactor must be greater than zero when supplied.");
        }

        EacCustomPerformanceFactor = performanceFactor;
    }

    public void SetEacManualEtc(decimal? manualEtc)
    {
        if (manualEtc is < 0)
        {
            throw new DomainException("EacManualEtc cannot be negative.");
        }

        EacManualEtc = manualEtc;

        // domain-rules.md §5.7: a QS re-entering the manual ETC is exactly the event that clears the
        // staleness this sprint's VO approval effect stamps - never cleared by anything else (in
        // particular, never by time passing or by a later VO of its own).
        EacManualEtcStaleSince = null;
    }

    /// <summary>
    /// S10-BE-03 (domain-rules.md §5.1): the two money moves an <c>Approved</c> Variation Order makes,
    /// both signed, applied together so they can never land only one of the two -
    /// <c>ApproveVariationOrderCommandHandler</c> calls this once, in the same unit of work as the
    /// <see cref="VariationOrder"/>'s own state change, never as two separate calls.
    /// <see cref="OriginalContractValue"/> and <see cref="OriginalBac"/> are both deliberately
    /// untouched - the former moves only via a formal contract amendment (domain-rules.md §4.4, not
    /// yet built), and domain-rules.md never contemplates the latter moving at all; both would defeat
    /// $BAC(t)$/escalation reconstruction if an approval silently rebased them.
    ///
    /// <para><b>Non-negativity (domain-rules.md §5.1's "Guard"):</b> a <c>Deduct</c> VO large enough to
    /// drive either figure below zero throws - the caller is expected to have already pre-validated
    /// this (returning a typed 422 <c>VoWouldMakeContractValueNegative</c> instead of ever reaching
    /// here), so this is defense-in-depth, not the primary guard, exactly like every other
    /// <see cref="MoneyGuard"/> use in this codebase. Never clamps: a silent clamp here would break the
    /// $\Delta B_{scope} = A$ invariant the scope-item deltas were separately validated against.</para>
    /// </summary>
    public void ApplyVariationOrderApproval(decimal amount, DateTimeOffset approvedAt)
    {
        BAC = MoneyGuard.EnsureNonNegative(BAC + amount, nameof(BAC));
        ContractValue = MoneyGuard.EnsureNonNegative(ContractValue + amount, nameof(ContractValue));
        ApprovedVariationOrderCount++;

        if (EacManualEtc is not null && EacManualEtcStaleSince is null)
        {
            EacManualEtcStaleSince = approvedAt;
        }
    }

    public void SetRetentionCapPercentage(decimal? capPercentage) =>
        RetentionCapPercentage = PercentageGuard.Clamp(capPercentage);

    public void SetRetentionRelease1Percentage(decimal percentage) =>
        RetentionRelease1Percentage = PercentageGuard.Clamp(percentage);

    public void SetDefectsLiabilityMonths(int? months)
    {
        if (months is < 0)
        {
            throw new DomainException("DefectsLiabilityMonths cannot be negative.");
        }

        DefectsLiabilityMonths = months;
    }

    public void SetAdvanceAmountPaid(decimal? amount) =>
        AdvanceAmountPaid = MoneyGuard.EnsureNonNegative(amount, nameof(AdvanceAmountPaid));

    public void SetAdvanceRecoveryMethod(
        AdvanceRecoveryMethod method,
        decimal? startPct = null,
        decimal? ratePct = null,
        decimal? endPct = null)
    {
        AdvanceRecoveryMethod = method;
        AdvanceRecoveryStartPct = PercentageGuard.Clamp(startPct);
        AdvanceRecoveryRatePct = PercentageGuard.Clamp(ratePct);
        AdvanceRecoveryEndPct = PercentageGuard.Clamp(endPct);
    }

    private static string ValidateRequired(string value, string propertyName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new DomainException($"{propertyName} is required.")
            : value.Trim();
}
