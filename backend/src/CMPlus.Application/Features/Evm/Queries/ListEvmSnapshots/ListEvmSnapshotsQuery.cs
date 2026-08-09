using CMPlus.Application.Features.Evm.Commands.CloseEvmPeriod;
using CMPlus.Domain.Common;
using MediatR;

namespace CMPlus.Application.Features.Evm.Queries.ListEvmSnapshots;

/// <summary>
/// `GET /api/v1/projects/{projectId}/evm/snapshots?from=&amp;to=` - the closed-period history
/// (design.md §2.1). Both bounds optional and inclusive; omitting both returns every closed period
/// for the project, ascending by data date.
///
/// <para>This read is in design.md's API contract but was missing from docs/10 §7's S7-BE-05 task
/// row, so it was not built with the rest of the backend row - caught during Sprint 7 review
/// because S7-FE-04's own DoD ("จุดก่อน data date มาจาก <c>EvmPeriodSnapshot</c> ไม่คำนวณสด") cannot
/// be satisfied without it: the S-Curve's historical points must come from frozen snapshots, not a
/// live recomputation, precisely because ADR-0009 makes a live recomputation legitimately change
/// when a backdated progress entry lands.</para>
///
/// <para>Reuses <see cref="EvmSnapshotDto"/> rather than defining a near-identical list DTO - a
/// closed period has exactly one wire shape, and having two would invite them to drift.</para>
/// </summary>
public sealed record ListEvmSnapshotsQuery(Guid ProjectId, DateTimeOffset? From, DateTimeOffset? To)
    : IRequest<Result<IReadOnlyList<EvmSnapshotDto>>>;
