using CMPlus.Domain.Common;
using MediatR;

namespace CMPlus.Application.Features.Cpm.Commands.RecalculateCpm;

/// <summary>
/// S5-BE-04 (US-5.1): the WBS/Gantt screen's "คำนวณ CPM ใหม่" (recalculate CPM) action - loads a
/// project's full activity/relation graph, runs <c>CpmEngine</c> (S5-BE-01..03), and writes
/// <c>IsCritical</c>/<c>TotalFloat</c>/<c>FreeFloat</c> back onto every <c>Activity</c> as one bulk
/// operation. Idempotent and side-effect-free on failure (a rejected graph - cycle/duplicate/
/// unknown-activity relation - never touches any `Activity` row; see
/// <see cref="RecalculateCpmCommandHandler"/>).
/// </summary>
public sealed record RecalculateCpmCommand(Guid ProjectId) : IRequest<Result<RecalculateCpmResultDto>>;
