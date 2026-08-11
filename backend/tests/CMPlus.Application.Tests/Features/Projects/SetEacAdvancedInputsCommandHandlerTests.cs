using CMPlus.Application.Abstractions;
using CMPlus.Application.Features.Projects;
using CMPlus.Application.Features.Projects.Commands.SetEacAdvancedInputs;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Tests.Features.Projects;

public class SetEacAdvancedInputsCommandHandlerTests
{
    private sealed class FakeProjectRepository : IProjectRepository
    {
        public Project? ProjectToReturn { get; set; }
        public int SaveChangesCallCount { get; private set; }
        public bool NextSaveHitsConcurrencyConflict { get; set; }

        public Task<Project?> FindAsync(Guid projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(ProjectToReturn);

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
        var handler = new SetEacAdvancedInputsCommandHandler(repository);

        var result = await handler.Handle(
            new SetEacAdvancedInputsCommand(Guid.NewGuid(), 760_000.00m, null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ProjectErrorCodes.NotFound, result.Error);
        Assert.Equal(0, repository.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_Sets_Both_Inputs_And_Saves_Once()
    {
        var project = CreateProject();
        var repository = new FakeProjectRepository { ProjectToReturn = project };
        var handler = new SetEacAdvancedInputsCommandHandler(repository);

        var result = await handler.Handle(
            new SetEacAdvancedInputsCommand(project.Id, 760_000.00m, 1.20m), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(760_000.00m, project.EacManualEtc);
        Assert.Equal(1.20m, project.EacCustomPerformanceFactor);
        Assert.Equal(1, repository.SaveChangesCallCount);
        Assert.Equal(760_000.00m, result.Value.EacManualEtc);
        Assert.Equal(1.20m, result.Value.EacCustomPerformanceFactor);
    }

    [Fact]
    public async Task Handle_Clears_A_Previously_Set_Input_When_The_Request_Omits_It_And_No_Variant_Needs_It()
    {
        // Full-representation PUT (SetEacAdvancedInputsCommand's own remarks): omitting a field
        // genuinely clears it, exactly like UpdateProjectCommand's other fields.
        var project = CreateProject();
        project.SetEacManualEtc(760_000.00m);
        var repository = new FakeProjectRepository { ProjectToReturn = project };
        var handler = new SetEacAdvancedInputsCommandHandler(repository);

        var result = await handler.Handle(
            new SetEacAdvancedInputsCommand(project.Id, null, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(project.EacManualEtc);
    }

    // ------------------------------------------------------------------------------------
    // S14-BE-03 DoD: "ตั้ง variant เป็น BottomUpEtc/CustomPf โดยไม่มีค่าที่จำเป็น -> 400" - this
    // handler's own direction of the same guard (clearing the input while its variant is already
    // active), the symmetric complement of SetEacVariantDefaultCommandHandlerTests' cases.
    // ------------------------------------------------------------------------------------

    [Fact]
    public async Task Handle_Returns_400_When_Clearing_EacManualEtc_While_BottomUpEtc_Is_The_Active_Variant()
    {
        var project = CreateProject();
        project.SetEacManualEtc(760_000.00m);
        project.SetEacVariantDefault(EacVariant.BottomUpEtc);
        var repository = new FakeProjectRepository { ProjectToReturn = project };
        var handler = new SetEacAdvancedInputsCommandHandler(repository);

        var result = await handler.Handle(
            new SetEacAdvancedInputsCommand(project.Id, null, null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ProjectErrorCodes.EacManualEtcRequiredForBottomUpEtc, result.Error);
        Assert.Equal(760_000.00m, project.EacManualEtc); // unchanged
        Assert.Equal(0, repository.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_Returns_400_When_Clearing_EacCustomPerformanceFactor_While_CustomPf_Is_The_Active_Variant()
    {
        var project = CreateProject();
        project.SetEacCustomPerformanceFactor(1.20m);
        project.SetEacVariantDefault(EacVariant.CustomPf);
        var repository = new FakeProjectRepository { ProjectToReturn = project };
        var handler = new SetEacAdvancedInputsCommandHandler(repository);

        var result = await handler.Handle(
            new SetEacAdvancedInputsCommand(project.Id, null, null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ProjectErrorCodes.EacCustomPerformanceFactorRequiredForCustomPf, result.Error);
        Assert.Equal(1.20m, project.EacCustomPerformanceFactor); // unchanged
        Assert.Equal(0, repository.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_Allows_Re_Supplying_The_Same_EacManualEtc_While_BottomUpEtc_Is_Active()
    {
        // Not a "clear" - the guard only blocks a null value while the variant needs it.
        var project = CreateProject();
        project.SetEacManualEtc(760_000.00m);
        project.SetEacVariantDefault(EacVariant.BottomUpEtc);
        var repository = new FakeProjectRepository { ProjectToReturn = project };
        var handler = new SetEacAdvancedInputsCommandHandler(repository);

        var result = await handler.Handle(
            new SetEacAdvancedInputsCommand(project.Id, 810_000.00m, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(810_000.00m, project.EacManualEtc);
    }

    // ------------------------------------------------------------------------------------
    // The EacManualEtcStaleSince interaction this task calls out explicitly: setting a fresh
    // EacManualEtc through this new command must clear the staleness flag - proving the Sprint 10
    // domain behaviour (Project.SetEacManualEtc) is actually reachable end-to-end now that a real
    // command finally calls it, not just exercised directly against the entity in a Domain test.
    // ------------------------------------------------------------------------------------

    [Fact]
    public async Task Handle_Clears_EacManualEtcStaleSince_When_Setting_A_Fresh_EacManualEtc()
    {
        var project = CreateProject();
        project.SetEacManualEtc(50_000_000.00m);
        project.ApplyVariationOrderApproval(1_000_000.00m, DateTimeOffset.Parse("2026-08-10T09:00:00+07:00"));
        Assert.NotNull(project.EacManualEtcStaleSince); // sanity: the VO made it stale first.

        var repository = new FakeProjectRepository { ProjectToReturn = project };
        var handler = new SetEacAdvancedInputsCommandHandler(repository);

        var result = await handler.Handle(
            new SetEacAdvancedInputsCommand(project.Id, 52_000_000.00m, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(project.EacManualEtcStaleSince);
        Assert.Null(result.Value.EacManualEtcStaleSince);
        Assert.Equal(52_000_000.00m, project.EacManualEtc);
    }
}
