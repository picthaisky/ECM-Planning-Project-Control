using CMPlus.Domain.Common;
using MediatR;

namespace CMPlus.Application.Features.Manpower.Queries.GetProductivityIndex;

/// <summary>
/// <c>GET /api/v1/projects/{projectId}/manpower-logs/productivity-index</c> (S12-BE-02, domain-rules.md
/// (manpower-equipment) §5). <paramref name="WbsNodeId"/>/<paramref name="ActivityId"/> narrow the
/// scope (§4.3 Tier 1 - <c>null</c>/<c>null</c> = the whole project); <paramref name="From"/> = null
/// means a cumulative read from project start (§5.2's bottom formula); <paramref name="To"/> is the
/// half-open period's inclusive upper bound (§4.5).
/// </summary>
public sealed record GetProductivityIndexQuery(
    Guid ProjectId,
    Guid? WbsNodeId,
    Guid? ActivityId,
    DateTimeOffset? From,
    DateTimeOffset To) : IRequest<Result<ProductivityIndexResponseDto>>;
