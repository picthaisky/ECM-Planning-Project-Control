using CMPlus.Application.Abstractions;
using CMPlus.Application.Features.Projects.Queries.GetProject;
using CMPlus.Application.Features.Projects.Queries.GetProjects;

namespace CMPlus.Application.Tests.Features.Projects;

public class GetProjectsQueryHandlerTests
{
    private sealed class FakeProjectReader : IProjectReader
    {
        public IReadOnlyList<ProjectListItemDto> ProjectsToReturn { get; set; } = [];

        public Task<IReadOnlyList<ProjectListItemDto>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(ProjectsToReturn);

        public Task<ProjectDetailDto?> GetDetailByIdAsync(Guid projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ProjectDetailDto?>(null);
    }

    [Fact]
    public async Task Handle_Returns_Whatever_The_Reader_Returns()
    {
        var projects = new List<ProjectListItemDto>
        {
            new(Guid.NewGuid(), "Riverside Condominium Tower B", "RCT-B", "Owner Co.",
                DateTimeOffset.Parse("2025-10-01T00:00:00Z"), DateTimeOffset.Parse("2027-03-31T00:00:00Z")),
        };
        var reader = new FakeProjectReader { ProjectsToReturn = projects };
        var handler = new GetProjectsQueryHandler(reader);

        var result = await handler.Handle(new GetProjectsQuery(), CancellationToken.None);

        Assert.Same(projects, result);
    }

    [Fact]
    public async Task Handle_Returns_An_Empty_List_Rather_Than_An_Error_When_The_Tenant_Has_No_Projects()
    {
        var reader = new FakeProjectReader();
        var handler = new GetProjectsQueryHandler(reader);

        var result = await handler.Handle(new GetProjectsQuery(), CancellationToken.None);

        Assert.Empty(result);
    }
}
