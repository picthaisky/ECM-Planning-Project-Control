using CMPlus.Domain.Common;
using CMPlus.Domain.Enums;
using MediatR;

namespace CMPlus.Application.Features.Cpm.Commands.RecalculateCpm;

/// <summary>
/// S5-BE-04 (US-5.1): the WBS/Gantt screen's "คำนวณ CPM ใหม่" (recalculate CPM) action - loads a
/// project's full activity/relation graph, runs <c>CpmEngine</c> (S5-BE-01..03), and writes
/// <c>IsCritical</c>/<c>TotalFloat</c>/<c>FreeFloat</c> back onto every <c>Activity</c> as one bulk
/// operation. Idempotent and side-effect-free on failure (a rejected graph - cycle/duplicate/
/// unknown-activity relation - never touches any `Activity` row, and never captures a
/// <c>CpmRun</c>; see <see cref="RecalculateCpmCommandHandler"/>).
///
/// <para>ADR-0019: a successful run also captures an append-only <c>CpmRun</c> snapshot.
/// <see cref="Trigger"/> records why - defaults to <see cref="CpmRunTrigger.Manual"/> since the
/// only wired caller today is the authenticated "คำนวณ CPM ใหม่" controller action; the other
/// members exist for future callers (a VO-approval re-trigger, a schedule re-import, a background
/// job) to pass explicitly without changing this command's shape again.</para>
/// </summary>
public sealed record RecalculateCpmCommand(Guid ProjectId, CpmRunTrigger Trigger = CpmRunTrigger.Manual)
    : IRequest<Result<RecalculateCpmResultDto>>;
