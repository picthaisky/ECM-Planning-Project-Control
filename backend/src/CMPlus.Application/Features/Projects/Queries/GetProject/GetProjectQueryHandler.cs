using CMPlus.Application.Abstractions;
using CMPlus.Domain.Common;
using MediatR;

namespace CMPlus.Application.Features.Projects.Queries.GetProject;

/// <summary>
/// Thin read: the projection + tenant scoping live in <see cref="IProjectReader.GetDetailByIdAsync"/>
/// (a single AsNoTracking query); a null result is the "not found in this tenant" 404, never an empty
/// success.
/// </summary>
public sealed class GetProjectQueryHandler(IProjectReader reader)
    : IRequestHandler<GetProjectQuery, Result<ProjectDetailDto>>
{
    public async Task<Result<ProjectDetailDto>> Handle(GetProjectQuery request, CancellationToken cancellationToken)
    {
        var project = await reader.GetDetailByIdAsync(request.ProjectId, cancellationToken);

        return project is null
            ? Result<ProjectDetailDto>.Failure(ProjectErrorCodes.NotFound)
            : Result<ProjectDetailDto>.Success(project);
    }
}
