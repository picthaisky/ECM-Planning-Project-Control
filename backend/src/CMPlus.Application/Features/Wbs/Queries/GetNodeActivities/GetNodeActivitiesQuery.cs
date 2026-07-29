using CMPlus.Application.Wbs;
using CMPlus.Domain.Common;
using MediatR;

namespace CMPlus.Application.Features.Wbs.Queries.GetNodeActivities;

/// <summary>
/// S4-BE-05 (fast-follow, US-4.5): the activity list for one WBS node
/// (<c>GET /api/v1/projects/{projectId}/wbs-nodes/{wbsNodeId}/activities</c>) that the batch-
/// progress grid needs to populate its rows from a node the user actually browsed to -
/// <see cref="Wbs.Queries.GetWbsTree.GetWbsTreeQuery"/> deliberately returns only a node-level
/// <c>ActivityCount</c> rollup, never the activities themselves.
/// </summary>
public sealed record GetNodeActivitiesQuery(Guid ProjectId, Guid WbsNodeId)
    : IRequest<Result<IReadOnlyList<ActivityForProgressDto>>>;
