using CMPlus.Domain.Common;
using MediatR;

namespace CMPlus.Application.Features.Baseline.Queries.CompareBaseline;

/// <summary>
/// S14-BE-02 (US-14.2): `GET /api/v1/projects/{projectId}/baselines/compare?baselineId=`. Per-
/// activity day/money delta (current − baseline), matching the prototype's "เปรียบเทียบแผนปัจจุบัน
/// vs {{ activeBlName }}" comparison. <paramref name="BaselineId"/> is optional and defaults to the
/// project's currently-active baseline (the prototype's literal behaviour - it never lets the
/// comparison target an inactive baseline); supplying it explicitly is a deliberate, low-cost
/// extension for a future "preview against a different saved baseline" need, not part of the
/// literal S14-BE-02 ask - flagged in the backend-developer report rather than silently added.
/// </summary>
public sealed record CompareBaselineQuery(Guid ProjectId, Guid? BaselineId)
    : IRequest<Result<BaselineComparisonDto>>;
