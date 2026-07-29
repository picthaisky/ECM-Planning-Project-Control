using CMPlus.Domain.Entities;

namespace CMPlus.Application.Import;

/// <summary>
/// Neutral parse result shared by <see cref="Abstractions.IXerScheduleParser"/> and
/// <see cref="Abstractions.IMspdiScheduleParser"/> (S3-BE-01/02): fully-constructed, not-yet-persisted
/// Domain entities, already wired together (an <see cref="Activity"/>'s <c>WbsNodeId</c> and an
/// <see cref="ActivityRelation"/>'s predecessor/successor ids all point at the <see cref="Guid"/>s
/// generated for the <see cref="WbsNodes"/>/<see cref="Activities"/> in this same result - the
/// source file's own ids (P6 <c>task_id</c>, MSPDI <c>UID</c>, ...) never leave the parser).
/// <c>TenantId</c> on every entity is whatever the caller passed the parser - inconsequential
/// beyond readability, since <c>CmPlusDbContext.SaveChanges</c> always re-stamps it from
/// <c>ITenantProvider</c> on insert (ADR-0002) regardless of the value an entity is constructed with.
/// </summary>
public sealed record ParsedSchedule(
    IReadOnlyList<WBSNode> WbsNodes,
    IReadOnlyList<Activity> Activities,
    IReadOnlyList<ActivityRelation> Relations)
{
    /// <summary>Total row count across all three entity kinds - what <c>FileImportJob.RowsImported</c>
    /// records for a successful schedule import.</summary>
    public int RowCount => WbsNodes.Count + Activities.Count + Relations.Count;
}
