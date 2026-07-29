using CMPlus.Application.Abstractions;
using CMPlus.Application.Wbs;
using CMPlus.Domain.Common;
using MediatR;

namespace CMPlus.Application.Features.Wbs.Queries.GetNodeActivities;

public sealed class GetNodeActivitiesQueryHandler(IWbsNodeActivitiesReader reader)
    : IRequestHandler<GetNodeActivitiesQuery, Result<IReadOnlyList<ActivityForProgressDto>>>
{
    public async Task<Result<IReadOnlyList<ActivityForProgressDto>>> Handle(
        GetNodeActivitiesQuery request, CancellationToken cancellationToken)
    {
        if (!await reader.NodeExistsInProjectAsync(request.ProjectId, request.WbsNodeId, cancellationToken))
        {
            return Result<IReadOnlyList<ActivityForProgressDto>>.Failure(WbsErrorCodes.NodeNotFound);
        }

        var activities = await reader.GetActivitiesForNodeAsync(request.WbsNodeId, cancellationToken);
        return Result<IReadOnlyList<ActivityForProgressDto>>.Success(activities);
    }
}
