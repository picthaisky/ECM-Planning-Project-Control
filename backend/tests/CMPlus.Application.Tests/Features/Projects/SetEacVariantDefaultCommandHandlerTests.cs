using CMPlus.Application.Abstractions;
using CMPlus.Application.Features.Projects;
using CMPlus.Application.Features.Projects.Commands.SetEacVariantDefault;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Tests.Features.Projects;

public class SetEacVariantDefaultCommandHandlerTests
{
    private sealed class FakeProjectRepository : IProjectRepository
    {
        public Project? ProjectToReturn { get; set; }
        public int SaveChangesCallCount { get; private set; }

        public Task<Project?> FindAsync(Guid projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(ProjectToReturn);

        public bool NextSaveHitsConcurrencyConflict { get; set; }

        public Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveChangesCallCount++;
            return Task.FromResult(!NextSaveHitsConcurrencyConflict);
        }
    }

    private static Project CreateProject() => Project.Create(
        Guid.NewGuid(), "Project P", "P-001", "Owner",
        DateTimeOffset.Parse("2026-01-01T00:00:00Z"), DateTimeOffset.Parse("2026-12-31T00:00:00Z"),
        bac: 1_000_000m, dataDate: DateTimeOffset.Parse("2026-06-01T00:00:00Z"));

    [Fact]
    public async Task Handle_Returns_NotFound_When_The_Project_Does_Not_Exist()
    {
        var repository = new FakeProjectRepository { ProjectToReturn = null };
        var handler = new SetEacVariantDefaultCommandHandler(repository);

        var result = await handler.Handle(new SetEacVariantDefaultCommand(Guid.NewGuid(), EacVariant.Atypical), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ProjectErrorCodes.NotFound, result.Error);
        Assert.Equal(0, repository.SaveChangesCallCount);
    }

    // BottomUpEtc/CustomPf are deliberately NOT in this theory any more (S14-BE-03): each needs its
    // own project-level input already configured, which CreateProject()'s fixture never sets - see
    // the dedicated tests below for both directions of that guard.
    [Theory]
    [InlineData(EacVariant.CpiBased)]
    [InlineData(EacVariant.Atypical)]
    [InlineData(EacVariant.CpiSpiBased)]
    public async Task Handle_Sets_The_Projects_Default_Variant_And_Saves_Once(EacVariant variant)
    {
        var project = CreateProject();
        var repository = new FakeProjectRepository { ProjectToReturn = project };
        var handler = new SetEacVariantDefaultCommandHandler(repository);

        var result = await handler.Handle(new SetEacVariantDefaultCommand(project.Id, variant), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(variant, project.EacVariantDefault);
        Assert.Equal(1, repository.SaveChangesCallCount);
        Assert.Equal(project.Id, result.Value.ProjectId);
        Assert.Equal(variant, result.Value.EacVariantDefault);
    }

    // ------------------------------------------------------------------------------------
    // S14-BE-03: BottomUpEtc/CustomPf each need their own project-level input already configured.
    // ------------------------------------------------------------------------------------

    [Fact]
    public async Task Handle_Returns_400_When_Switching_To_BottomUpEtc_Without_EacManualEtc_Configured()
    {
        var project = CreateProject();
        var repository = new FakeProjectRepository { ProjectToReturn = project };
        var handler = new SetEacVariantDefaultCommandHandler(repository);

        var result = await handler.Handle(new SetEacVariantDefaultCommand(project.Id, EacVariant.BottomUpEtc), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ProjectErrorCodes.EacManualEtcRequiredForBottomUpEtc, result.Error);
        Assert.Equal(EacVariant.CpiBased, project.EacVariantDefault); // unchanged
        Assert.Equal(0, repository.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_Returns_400_When_Switching_To_CustomPf_Without_EacCustomPerformanceFactor_Configured()
    {
        var project = CreateProject();
        var repository = new FakeProjectRepository { ProjectToReturn = project };
        var handler = new SetEacVariantDefaultCommandHandler(repository);

        var result = await handler.Handle(new SetEacVariantDefaultCommand(project.Id, EacVariant.CustomPf), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ProjectErrorCodes.EacCustomPerformanceFactorRequiredForCustomPf, result.Error);
        Assert.Equal(EacVariant.CpiBased, project.EacVariantDefault); // unchanged
        Assert.Equal(0, repository.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_Switches_To_BottomUpEtc_When_EacManualEtc_Is_Already_Configured()
    {
        var project = CreateProject();
        project.SetEacManualEtc(760_000.00m);
        var repository = new FakeProjectRepository { ProjectToReturn = project };
        var handler = new SetEacVariantDefaultCommandHandler(repository);

        var result = await handler.Handle(new SetEacVariantDefaultCommand(project.Id, EacVariant.BottomUpEtc), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(EacVariant.BottomUpEtc, project.EacVariantDefault);
        Assert.Equal(1, repository.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_Switches_To_CustomPf_When_EacCustomPerformanceFactor_Is_Already_Configured()
    {
        var project = CreateProject();
        project.SetEacCustomPerformanceFactor(1.20m);
        var repository = new FakeProjectRepository { ProjectToReturn = project };
        var handler = new SetEacVariantDefaultCommandHandler(repository);

        var result = await handler.Handle(new SetEacVariantDefaultCommand(project.Id, EacVariant.CustomPf), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(EacVariant.CustomPf, project.EacVariantDefault);
        Assert.Equal(1, repository.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_Never_Reaches_Any_Aggregate_Other_Than_Project()
    {
        // ADR-0007(f): a mutating domain operation on Project only - IProjectRepository exposes
        // nothing else this handler could reach, the same "by construction" guarantee
        // UpdateProjectCommandHandlerTests documents for its own command.
        var project = CreateProject();
        var repository = new FakeProjectRepository { ProjectToReturn = project };
        var handler = new SetEacVariantDefaultCommandHandler(repository);

        var result = await handler.Handle(new SetEacVariantDefaultCommand(project.Id, EacVariant.CpiSpiBased), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }
}
