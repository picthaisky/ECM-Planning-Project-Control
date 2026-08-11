using CMPlus.Domain.Enums;

namespace CMPlus.Application.Services.Cpm;

/// <summary>
/// S5-BE-02: the forward/backward constraint formulas for all four CPM relation types with lag
/// (FS/SS/FF/SF, per <see cref="RelationType"/>/<c>ActivityRelation.LagDays</c> from Sprint 1),
/// per cpm-method.md step 2 ("FS: EF_p + L · SS: ES_p + L · FF: EF_i &gt;= EF_p + L ·
/// SF: EF_i &gt;= ES_p + L") and step 3 ("LF_i = min over successors (FS: LS_s - L, etc.)"). Pure
/// functions over plain working-day-count ints, no calendar/date concerns at all (see
/// <see cref="WorkingCalendar"/> for that separate half of S5-BE-02).
/// </summary>
public static class RelationConstraints
{
    /// <summary>
    /// The minimum <c>ES</c> the successor may take on account of this one predecessor edge - the
    /// forward pass takes the max of this across every predecessor edge into a given successor.
    /// Solved directly from cpm-method.md's own per-type definitions:
    /// FS <c>ES_s &gt;= EF_p + L</c> · SS <c>ES_s &gt;= ES_p + L</c> ·
    /// FF <c>EF_s &gt;= EF_p + L</c> (i.e. <c>ES_s &gt;= EF_p + L - D_s</c>) ·
    /// SF <c>EF_s &gt;= ES_p + L</c> (i.e. <c>ES_s &gt;= ES_p + L - D_s</c>).
    /// </summary>
    public static int ForwardConstraint(
        int predecessorEs, int predecessorEf, RelationType relationType, int lagDays, int successorDurationDays) =>
        relationType switch
        {
            RelationType.FS => predecessorEf + lagDays,
            RelationType.SS => predecessorEs + lagDays,
            RelationType.FF => predecessorEf + lagDays - successorDurationDays,
            RelationType.SF => predecessorEs + lagDays - successorDurationDays,
            _ => throw new ArgumentOutOfRangeException(nameof(relationType), relationType, "Unsupported CPM relation type."),
        };

    /// <summary>
    /// The maximum <c>LF</c> the predecessor may take on account of this one successor edge - the
    /// backward pass takes the min of this across every successor edge out of a given predecessor.
    /// This is the algebraic dual of <see cref="ForwardConstraint"/> (solved for the predecessor's
    /// own LF/LS instead of the successor's ES/EF), not itself spelled out numerically in
    /// cpm-method.md beyond the FS case - derivation, verified against the canonical fixture by
    /// backend-developer (Sprint 5 report has the full worked derivation):
    /// FS <c>LF_p &lt;= LS_s - L</c> ·
    /// SS <c>LS_p &lt;= LS_s - L</c> (i.e. <c>LF_p &lt;= LS_s - L + D_p</c>, since <c>LS_p = LF_p - D_p</c>) ·
    /// FF <c>LF_p &lt;= LF_s - L</c> ·
    /// SF <c>LS_p &lt;= LF_s - L</c> (i.e. <c>LF_p &lt;= LF_s - L + D_p</c>).
    /// </summary>
    public static int BackwardConstraint(
        int successorLs, int successorLf, RelationType relationType, int lagDays, int predecessorDurationDays) =>
        relationType switch
        {
            RelationType.FS => successorLs - lagDays,
            RelationType.SS => successorLs - lagDays + predecessorDurationDays,
            RelationType.FF => successorLf - lagDays,
            RelationType.SF => successorLf - lagDays + predecessorDurationDays,
            _ => throw new ArgumentOutOfRangeException(nameof(relationType), relationType, "Unsupported CPM relation type."),
        };
}
