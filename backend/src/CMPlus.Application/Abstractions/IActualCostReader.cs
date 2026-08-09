namespace CMPlus.Application.Abstractions;

/// <summary>
/// Resolves <c>AC</c> (ACWP) for a project as of a data date -
/// .claude/knowledge/domain/actual-cost.md §7.1/§9 (ADR-0013):
/// <c>AC(t) = SUM(Amount) WHERE IncurredDate &lt;= t</c>, tenant- and project-scoped. Every
/// <c>EntryType</c> (Actual/Accrual/AccrualReversal/Adjustment) is included in the sum - the sign
/// on <c>Amount</c> already carries the semantics, so the sum is a pure aggregate.
///
/// <para><b>Sprint 7 -&gt; Sprint 8 history:</b> until <see cref="CMPlus.Domain.Entities.ActualCostEntry"/>
/// landed, no cost-ledger entity existed anywhere in this codebase and the Infrastructure
/// implementation was an honest placeholder always returning a literal <c>0</c> (see the Sprint 7
/// backend-developer report and ADR-0013's context). That gap is now closed; the Infrastructure
/// implementation (<c>ActualCostReader</c>) is a real, indexed query against
/// <see cref="CMPlus.Domain.Entities.ActualCostEntry"/>.</para>
/// </summary>
public interface IActualCostReader
{
    /// <summary>
    /// <paramref name="cancellationToken"/> aside, resolves both the summed <c>Amount</c> and the
    /// number of contributing rows as of <paramref name="asOf"/> - see <see cref="ActualCostResult"/>
    /// for why the count matters. Tenant scoping comes from the global EF query filter (ADR-0002);
    /// <paramref name="projectId"/> additionally scopes to one project within that tenant.
    /// </summary>
    Task<ActualCostResult> GetActualCostAsOfAsync(Guid projectId, DateTimeOffset asOf, CancellationToken cancellationToken = default);
}

/// <summary>
/// actual-cost.md §9's recommended reader shape, adopted verbatim by ADR-0013(f): returning the
/// entry count alongside the amount is what lets a caller distinguish "no cost recorded yet"
/// (<see cref="EntryCount"/> 0, <see cref="Amount"/> 0.00 - fixture AC-5) from "costs recorded,
/// netting to exactly zero" (<see cref="EntryCount"/> &gt; 0, <see cref="Amount"/> 0.00 - fixture
/// AC-6, e.g. a month-end accrual fully reversed with the real invoice not yet posted). Both cases
/// still resolve to the same <c>EacNullReason.NoActualCost</c> (actual-cost.md §7.6 - "no breaking
/// change to the S7 contract"); the count exists so a consumer's warning copy can honestly say
/// which situation it is, rather than both reading as an unqualified "no cost".
///
/// <para><see cref="Amount"/> may legitimately be negative (an over-reversal/credit note exceeding
/// recorded costs, actual-cost.md §7.6) - the reader never clamps it.</para>
/// </summary>
public sealed record ActualCostResult(decimal Amount, int EntryCount);
