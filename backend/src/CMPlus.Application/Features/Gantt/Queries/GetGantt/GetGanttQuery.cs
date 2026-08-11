using CMPlus.Domain.Common;
using MediatR;

namespace CMPlus.Application.Features.Gantt.Queries.GetGantt;

/// <summary>
/// S6-BE-01 (US-6.1): the Gantt chart read (<c>GET /api/v1/projects/{id}/gantt</c>) S6-FE-01/02
/// render against. Returns only what a virtualized Gantt (ADR-0004) needs to draw a bar per
/// activity, ordered to align with the WBS tree's row order (docs/10 §7 DoD) - never the full
/// <c>Activity</c> entity and never a nested tree shape of its own (see
/// <see cref="GanttRowOrderer"/>'s remarks).
///
/// <para><paramref name="From"/>/<paramref name="To"/> are the "รองรับ query ตามช่วงวันที่" (support
/// querying by date range) requirement - an optional, independently-one-sided time window that
/// filters *which activities* are returned (by planned-span overlap; see
/// <see cref="CMPlus.Application.Abstractions.IGanttActivityReader"/>'s remarks for why), so a very
/// long project's Gantt does not have to return every activity in one response when the frontend is
/// only rendering a zoomed-in slice of the timeline. Omitting both returns the whole project's
/// schedule, unwindowed.</para>
/// </summary>
public sealed record GetGanttQuery(Guid ProjectId, DateTimeOffset? From, DateTimeOffset? To)
    : IRequest<Result<GanttDto>>;
