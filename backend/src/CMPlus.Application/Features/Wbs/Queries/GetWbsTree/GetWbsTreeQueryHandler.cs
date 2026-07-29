using CMPlus.Application.Abstractions;
using CMPlus.Application.Wbs;
using CMPlus.Domain.Common;
using MediatR;

namespace CMPlus.Application.Features.Wbs.Queries.GetWbsTree;

public sealed class GetWbsTreeQueryHandler(IWbsTreeReader reader)
    : IRequestHandler<GetWbsTreeQuery, Result<WbsTreeDto>>
{
    public async Task<Result<WbsTreeDto>> Handle(GetWbsTreeQuery request, CancellationToken cancellationToken)
    {
        if (!await reader.ProjectExistsAsync(request.ProjectId, cancellationToken))
        {
            return Result<WbsTreeDto>.Failure(WbsErrorCodes.ProjectNotFound);
        }

        // Exactly two bounded queries total (node shape + activity-count rollup, both scoped to
        // this project) happen inside GetNodesWithActivityCountsAsync - never one query per node
        // (the N+1 trap S4-BE-01 explicitly forbids). Nesting into the response tree is then pure,
        // in-memory work (WbsTreeBuilder), contributing no further I/O.
        var flatNodes = await reader.GetNodesWithActivityCountsAsync(request.ProjectId, cancellationToken);
        return Result<WbsTreeDto>.Success(WbsTreeBuilder.Build(request.ProjectId, flatNodes));
    }
}
