using CMPlus.Domain.Common;

namespace CMPlus.Domain.Entities;

/// <summary>
/// One line of a <see cref="VariationOrder"/>'s scope payload - domain-rules.md §5.2's "delta list":
/// <c>{ActivityId, BudgetCostDelta}</c>, never a list of added activities carrying their own
/// (potentially negative) budget. This is the structural reason a <c>Deduct</c> VO can exist at all
/// despite <see cref="Activity.BudgetCost"/> being <see cref="MoneyGuard.EnsureNonNegative"/>: the
/// delta is applied to an already-non-negative existing balance
/// (<see cref="Activity.AdjustBudgetCost"/>), never used to construct a negative-budget activity.
///
/// <para><b>Sprint 10 scope note (disclosed deviation - see the backend-developer handoff report):
/// </b> this sprint's scope payload targets only <i>existing</i> activities. Domain-rules.md §5.2
/// also describes a <c>{ActivityId | new activity spec, BudgetCostDelta}</c> shape where a VO can
/// introduce a brand-new schedulable activity; that half needs WBS placement/duration/calendar
/// fields with no fixture in domain-rules.md pinning their shape, and is left for the WBS/Activity
/// module to define rather than guessed here. The $\Delta B_{scope} = A$ invariant, the
/// retroactive-earned-value guard (<c>VoOmitsExecutedScope</c>) and the BAC/PV effects are all fully
/// enforced for this (existing-activity) shape.</para>
///
/// <para>Frozen together with <see cref="VariationOrder.Amount"/> from <c>PendingApproval</c>
/// onward (field-freeze matrix, domain-rules.md §2.4) - the whole collection is replaced, never
/// edited in place, by <see cref="VariationOrder.SetVariationContent"/>, the sole writer.</para>
///
/// <para><b>M-01 (sprint-10 security review):</b> <see cref="INeverModified"/> - the identical shape
/// already applied to <see cref="VariationOrderApprovalStep"/>. Before this marker, a raw
/// <c>BudgetCostDelta</c> rewrite through an ordinary <c>CmPlusDbContext</c> on a
/// <c>PendingApproval</c> VO persisted with no error at all, desynchronising <c>Activity.BudgetCost</c>
/// (and hence $EV$/$PV$) from <see cref="VariationOrder.Amount"/> (and hence <see cref="Project.BAC"/>)
/// permanently and invisibly to any test that only looks at <c>Project.BAC</c> - domain-rules.md
/// §2.4's own instruction ("The money/scope freeze must be enforced at the persistence layer too").
/// <see cref="VariationOrder.SetVariationContent"/> clears and re-adds the whole collection, so
/// <c>Added</c>/<c>Deleted</c> stay legal - only a <c>Modified</c> update to an existing row is
/// blocked.</para>
/// </summary>
public sealed class VariationOrderScopeItem : Entity, ITenantOwned, INeverModified
{
    public Guid TenantId { get; private set; }

    public Guid VariationOrderId { get; private set; }

    public Guid ActivityId { get; private set; }

    /// <summary>Signed <c>decimal(18,2)</c>; never zero (a no-op line is noise - the same "do not
    /// post a 0.00 row" discipline <see cref="ProjectFinanceLedger"/> already established). The
    /// <i>sum</i> of every item on one <see cref="VariationOrder"/> must equal
    /// <see cref="VariationOrder.Amount"/> exactly (domain-rules.md §5.2) - enforced by
    /// <see cref="VariationOrder.SetVariationContent"/>, not here (a single line has no way to know
    /// the aggregate's total).</summary>
    public decimal BudgetCostDelta { get; private set; }

    public string? Note { get; private set; }

    // EF Core materialization fallback - see Project.cs's remark on why every entity keeps one.
    private VariationOrderScopeItem()
    {
    }

    internal VariationOrderScopeItem(
        Guid tenantId,
        Guid variationOrderId,
        Guid activityId,
        decimal budgetCostDelta,
        string? note)
    {
        if (variationOrderId == Guid.Empty)
        {
            throw new DomainException("VariationOrderScopeItem.VariationOrderId is required.");
        }

        if (activityId == Guid.Empty)
        {
            throw new DomainException("VariationOrderScopeItem.ActivityId is required.");
        }

        if (budgetCostDelta == 0m)
        {
            throw new DomainException("VariationOrderScopeItem.BudgetCostDelta cannot be zero (a no-op scope line is noise).");
        }

        if (budgetCostDelta != RoundingRules.ToMoney(budgetCostDelta))
        {
            throw new DomainException("VariationOrderScopeItem.BudgetCostDelta cannot have more than 2 decimal places.");
        }

        TenantId = tenantId;
        VariationOrderId = variationOrderId;
        ActivityId = activityId;
        BudgetCostDelta = budgetCostDelta;
        Note = note;
    }
}

/// <summary>
/// Plain data shape for one scope line supplied to <see cref="VariationOrder.SetVariationContent"/> -
/// mirrors <see cref="VariationOrderApprovalStepInput"/>'s "input shape before it becomes a child
/// entity" pattern.
/// </summary>
public sealed record VariationOrderScopeItemInput(Guid ActivityId, decimal BudgetCostDelta, string? Note = null);
