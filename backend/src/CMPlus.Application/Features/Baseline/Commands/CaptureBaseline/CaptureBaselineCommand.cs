using CMPlus.Domain.Common;
using MediatR;

namespace CMPlus.Application.Features.Baseline.Commands.CaptureBaseline;

/// <summary>
/// S14-BE-01 (US-14.1): `POST /api/v1/projects/{projectId}/baselines`. Snapshots every current
/// <see cref="CMPlus.Domain.Entities.Activity"/>'s planned dates/duration/budget under a fresh,
/// inactive <see cref="CMPlus.Domain.Entities.Baseline"/> - never active on creation, mirroring the
/// prototype's two separate actions ("+ บันทึก Baseline ใหม่" vs "ตั้งเป็น Active" -
/// <c>ActivateBaselineCommand</c> is the second, deliberate step). Restricted to
/// PM/Planning/Admin at the WebApi boundary - the same audience <c>CpmController</c>'s "คำนวณ CPM
/// ใหม่" gate already uses, for the same reason: capturing a baseline is a scheduling/reference-
/// setting operation over the same schedule data CPM recalculation reads, not something Site/QS/
/// Executive trigger. Audited automatically (one summarizing row, see
/// <c>IBaselineRepository.AddAsync</c>'s remarks).
/// </summary>
public sealed record CaptureBaselineCommand(Guid ProjectId, string Name)
    : IRequest<Result<BaselineDto>>;
