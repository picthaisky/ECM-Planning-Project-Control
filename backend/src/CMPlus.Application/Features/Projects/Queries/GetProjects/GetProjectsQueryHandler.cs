using CMPlus.Application.Abstractions;
using MediatR;

namespace CMPlus.Application.Features.Projects.Queries.GetProjects;

public sealed class GetProjectsQueryHandler(IProjectReader reader)
    : IRequestHandler<GetProjectsQuery, IReadOnlyList<ProjectListItemDto>>
{
    public Task<IReadOnlyList<ProjectListItemDto>> Handle(GetProjectsQuery request, CancellationToken cancellationToken) =>
        reader.GetAllAsync(cancellationToken);
}
