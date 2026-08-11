using CMPlus.Application.Abstractions;
using CMPlus.Domain.Common;
using MediatR;

namespace CMPlus.Application.Features.Gantt.Queries.GetGantt;

/// <summary>
/// S6-BE-01: loads the WBS hierarchy (reused from <see cref="IWbsTreeReader"/> - the exact same
/// reader/query <c>GetWbsTreeQueryHandler</c> uses, see that interface's remarks) and the
/// (optionally date-windowed) activity bars from <see cref="IGanttActivityReader"/>, then hands both
/// flat row sets to the pure, in-memory <see cref="GanttRowOrderer"/>. Exactly two bounded reads
/// total, matching the "hierarchical fetch in one query [per read]" discipline
/// <c>GetWbsTreeQueryHandler</c> already established - never one query per node/activity.
/// </summary>
public sealed class GetGanttQueryHandler(IWbsTreeReader wbsTreeReader, IGanttActivityReader activityReader)
    : IRequestHandler<GetGanttQuery, Result<GanttDto>>
{
    public async Task<Result<GanttDto>> Handle(GetGanttQuery request, CancellationToken cancellationToken)
    {
        if (!await wbsTreeReader.ProjectExistsAsync(request.ProjectId, cancellationToken))
        {
            return Result<GanttDto>.Failure(GanttErrorCodes.ProjectNotFound);
        }

        var nodes = await wbsTreeReader.GetNodesWithActivityCountsAsync(request.ProjectId, cancellationToken);
        var activities = await activityReader.GetActivitiesAsync(
            request.ProjectId, request.From, request.To, cancellationToken);
        var dataDate = await activityReader.GetProjectDataDateAsync(request.ProjectId, cancellationToken);

        var dto = GanttRowOrderer.Build(request.ProjectId, nodes, activities) with { DataDate = dataDate };
        return Result<GanttDto>.Success(dto);
    }
}
