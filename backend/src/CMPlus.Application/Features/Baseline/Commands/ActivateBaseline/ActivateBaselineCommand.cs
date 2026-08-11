using CMPlus.Domain.Common;
using MediatR;

namespace CMPlus.Application.Features.Baseline.Commands.ActivateBaseline;

/// <summary>
/// S14-BE-01 (US-14.1): `POST /api/v1/projects/{projectId}/baselines/{baselineId}/activate`. Makes
/// <paramref name="BaselineId"/> the project's sole active baseline, deactivating whichever baseline
/// currently holds that role (if any) in the same unit of work - see
/// <c>ActivateBaselineCommandHandler</c>'s remarks for the ordering this depends on and what remains
/// unproven in this environment. Same role gate as <c>CaptureBaselineCommand</c> (PM/Planning/Admin)
/// and audited automatically (default per-entity behaviour - at most two rows change).
/// </summary>
public sealed record ActivateBaselineCommand(Guid ProjectId, Guid BaselineId)
    : IRequest<Result<ActivateBaselineResultDto>>;
