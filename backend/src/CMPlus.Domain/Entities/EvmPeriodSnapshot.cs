using CMPlus.Domain.Common;
using CMPlus.Domain.Enums;

namespace CMPlus.Domain.Entities;

/// <summary>
/// S7-BE-05: an immutable, append-only record of a closed EVM reporting period (ADR-0009) - "what
/// the dashboard/S-Curve/payment certification would have shown for this data date", frozen so a
/// later backdated <see cref="ActivityProgressLog"/> correction (which legitimately changes a *live*
/// recomputation) can never silently rewrite an already-issued report. Captures the
/// <see cref="EacVariant"/>/<see cref="PerformanceFactor"/> actually used at closing time (always
/// `Project.EacVariantDefault` as of that moment - never a per-request override, since the close
/// endpoint's request body is only a data date per design.md §2.1) so an old snapshot stays
/// explainable even after the project's default variant later changes.
///
/// <para>Same append-only discipline as <see cref="ApprovalAction"/>/<see cref="ActivityProgressLog"/>:
/// no mutator method and no public property setter anywhere - verified structurally, not merely by
/// convention, in <c>CMPlus.Domain.Tests</c>. There is deliberately no public/internal way to
/// construct one except this single constructor; corrections are a new closed period, never an edit
/// of this row (S7-BE-05 DoD: "immutable - no update path at all").</para>
///
/// <para><see cref="PerformanceFactor"/>/<see cref="Eac"/>/<see cref="Etc"/>/<see cref="Vac"/> are
/// nullable: the project's selected variant may itself be non-computable at the data date being
/// closed (e.g. closing period one of a not-started project - see <see cref="EacNullReason.NotStarted"/>)
/// and a snapshot must be able to freeze that "no answer, and here is why" state exactly as the live
/// endpoint would have shown it, never substituting a fabricated 0.00 (the same non-negotiable
/// evm-formulas.md states for the live query).</para>
/// </summary>
public sealed class EvmPeriodSnapshot : Entity, ITenantOwned, IAppendOnly
{
    public Guid TenantId { get; private set; }

    public Guid ProjectId { get; private set; }

    public DateTimeOffset DataDate { get; private set; }

    public decimal Bac { get; private set; }

    public decimal Pv { get; private set; }

    public decimal Ev { get; private set; }

    public decimal Ac { get; private set; }

    /// <summary><c>Project.EacVariantDefault</c> as of the moment this period was closed.</summary>
    public EacVariant EacVariant { get; private set; }

    /// <summary><c>decimal(9,6)</c> (design.md §3) - null when <see cref="EacVariant"/> was not
    /// computable at <see cref="DataDate"/> (see this type's remarks), or when it is
    /// <see cref="Domain.Enums.EacVariant.BottomUpEtc"/> (no PF is derived for that variant).</summary>
    public decimal? PerformanceFactor { get; private set; }

    public decimal? Eac { get; private set; }

    public decimal? Etc { get; private set; }

    public decimal? Vac { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Null for a system/seed-time close with no authenticated actor (mirrors
    /// <see cref="AuditLog.UserId"/>'s own nullability reasoning).</summary>
    public Guid? CreatedByUserId { get; private set; }

    // EF Core materialization fallback - see Project.cs's remark on why every entity keeps one.
    private EvmPeriodSnapshot()
    {
    }

    /// <summary>
    /// Every money/PF value is expected to already be rounded by <see cref="RoundingRules"/> at the
    /// caller's single rounding boundary (this constructor re-applies the same rounding defensively,
    /// exactly like <c>Project</c>'s constructor re-applies <see cref="MoneyGuard"/>/
    /// <see cref="PercentageGuard"/> even though callers are expected to have already validated -
    /// belt-and-braces so a stored row can never carry more precision than its column, regardless of
    /// caller discipline).
    /// </summary>
    public EvmPeriodSnapshot(
        Guid tenantId,
        Guid projectId,
        DateTimeOffset dataDate,
        decimal bac,
        decimal pv,
        decimal ev,
        decimal ac,
        EacVariant eacVariant,
        decimal? performanceFactor,
        decimal? eac,
        decimal? etc,
        decimal? vac,
        DateTimeOffset createdAt,
        Guid? createdByUserId)
    {
        if (projectId == Guid.Empty)
        {
            throw new DomainException("EvmPeriodSnapshot.ProjectId is required.");
        }

        TenantId = tenantId;
        ProjectId = projectId;
        DataDate = dataDate;
        Bac = MoneyGuard.EnsureNonNegative(RoundingRules.ToMoney(bac), nameof(Bac));
        Pv = MoneyGuard.EnsureNonNegative(RoundingRules.ToMoney(pv), nameof(Pv));
        Ev = MoneyGuard.EnsureNonNegative(RoundingRules.ToMoney(ev), nameof(Ev));

        // Latent defect fixed while implementing ActualCostEntry (found by qa-engineer in Sprint 7
        // review, recorded in ADR-0013's consequences): AC was NOT guarded non-negative like
        // Bac/Pv/Ev used to be. That was unreachable while IActualCostReader was pinned at a
        // literal 0, but a real ActualCostEntry ledger can legitimately produce a negative AC(t)
        // (a credit note or over-reversal exceeding recorded costs - actual-cost.md §6.5/§7.6,
        // "compute and return as-is; do not clamp"), and closing a period must degrade gracefully
        // to that negative value rather than throw DomainException. Ac now gets the same treatment
        // this constructor already gives Eac/Etc/Vac below.
        Ac = RoundingRules.ToMoney(ac);
        EacVariant = eacVariant;

        // ETC/EAC/VAC are deliberately NOT guarded non-negative: a negative ETC (EV > BAC, per
        // evm-formulas.md's edge-case table) and a negative VAC (a forecast overrun - the common
        // case) are both legitimate, expected values, not invariant violations.
        PerformanceFactor = RoundingRules.ToRatio(performanceFactor);
        Eac = RoundingRules.ToMoney(eac);
        Etc = RoundingRules.ToMoney(etc);
        Vac = RoundingRules.ToMoney(vac);
        CreatedAt = createdAt;
        CreatedByUserId = createdByUserId;
    }
}
