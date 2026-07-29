using CMPlus.Application.Abstractions;
using CMPlus.Application.Common;
using CMPlus.Application.Features.Projects.Commands.UpdateProject;
using CMPlus.Domain.Common;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using MediatR;

namespace CMPlus.Application.Tests.Features.Projects;

/// <summary>
/// S4-QA-02 (docs/10 §6): "verify this two-layer behaviour is what's actually implemented and test
/// both layers explicitly, don't assume." <see cref="UpdateProjectCommandValidator"/>'s own remarks
/// state the intended split: the validator is "the layer that turns an out-of-range value into an
/// explicit rejection with a message, rather than a silently-clamped save"; every percent setter on
/// <see cref="Project"/> independently clamps via <c>PercentageGuard.Clamp</c> as a defense-in-depth
/// backstop. These tests exercise both layers directly against the real production types (the real
/// <see cref="ValidationBehavior{TRequest,TResponse}"/>, the real validator, the real handler) rather
/// than re-describing the intent - and they document a real asymmetry: an out-of-range rate is
/// *clamped* if it ever reached the handler bypassing the validator, whereas an invalid date range is
/// *thrown* in the same bypass scenario (<see cref="UpdateProjectCommandHandlerTests"/>'s
/// <c>Handle_Propagates_The_Domain_Invariant_When_ContractFinish_Precedes_ContractStart</c>) - two
/// different defense-in-depth behaviours for two different fields, both deliberate, neither
/// interchangeable, and easy to get backwards without a test pinning each one down.
/// </summary>
public class UpdateProjectCommandTwoLayerBehaviorTests
{
    private sealed class FakeProjectRepository : IProjectRepository
    {
        public Project? ProjectToReturn { get; set; }
        public int FindAsyncCallCount { get; private set; }
        public int SaveChangesCallCount { get; private set; }

        public Task<Project?> FindAsync(Guid projectId, CancellationToken cancellationToken = default)
        {
            FindAsyncCallCount++;
            return Task.FromResult(ProjectToReturn);
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveChangesCallCount++;
            return Task.CompletedTask;
        }
    }

    private static Project CreateProject() => Project.Create(
        Guid.NewGuid(), "Original Name", "P-001", "Original Owner",
        DateTimeOffset.Parse("2026-01-01T00:00:00Z"), DateTimeOffset.Parse("2026-12-31T00:00:00Z"),
        bac: 1_000_000m, dataDate: DateTimeOffset.Parse("2026-06-01T00:00:00Z"), retentionRate: 5.00m, advanceRate: 10.00m);

    private static UpdateProjectCommand ValidCommand(Guid projectId) => new(
        ProjectId: projectId,
        Name: "Updated Name",
        Code: "P-002",
        Owner: "Updated Owner Co.",
        ContractStart: DateTimeOffset.Parse("2026-02-01T00:00:00Z"),
        ContractFinish: DateTimeOffset.Parse("2027-01-31T00:00:00Z"),
        Bac: 1_500_000m,
        ContractValue: 1_450_000m,
        RetentionRate: 5.00m,
        AdvanceRate: 10.00m,
        RetentionCapPercentage: 10.00m,
        RetentionRelease1Percentage: 50.00m,
        DefectsLiabilityMonths: 12,
        AdvanceAmountPaid: null,
        AdvanceRecoveryMethod: AdvanceRecoveryMethod.ProRata,
        AdvanceRecoveryStartPct: null,
        AdvanceRecoveryRatePct: null,
        AdvanceRecoveryEndPct: null);

    /// <summary>
    /// Runs the real <see cref="ValidationBehavior{TRequest,TResponse}"/> (MediatR's pipeline
    /// behavior, exactly as <c>DependencyInjection.AddApplication</c> registers it) wrapping the
    /// real handler, without needing a full DI container - <c>ValidationBehavior.Handle</c> only
    /// needs an <see cref="IEnumerable{T}"/> of validators and a <c>next</c> delegate, both supplied
    /// directly here.
    /// </summary>
    private static Task<Result<ProjectDto>> RunThroughPipeline(
        UpdateProjectCommand command, FakeProjectRepository repository)
    {
        var behavior = new ValidationBehavior<UpdateProjectCommand, Result<ProjectDto>>(
            [new UpdateProjectCommandValidator()]);
        var handler = new UpdateProjectCommandHandler(repository);

        RequestHandlerDelegate<Result<ProjectDto>> next = ct => handler.Handle(command, ct);

        return behavior.Handle(command, next, CancellationToken.None);
    }

    [Theory]
    [InlineData(150)]
    [InlineData(-1)]
    public async Task Pipeline_Rejects_OutOfRange_RetentionRate_Before_The_Handler_Ever_Runs(decimal outOfRangeRate)
    {
        var project = CreateProject();
        var repository = new FakeProjectRepository { ProjectToReturn = project };
        var command = ValidCommand(project.Id) with { RetentionRate = outOfRangeRate };

        var result = await RunThroughPipeline(command, repository);

        Assert.True(result.IsFailure);
        Assert.Contains("RetentionRate", result.Error);
        // The handler (and therefore Project.SetRetentionRate's clamp) was never reached - the
        // validator is a true gate, not merely a client-side convenience that the domain also
        // happens to correct for.
        Assert.Equal(0, repository.FindAsyncCallCount);
        Assert.Equal(0, repository.SaveChangesCallCount);
        Assert.Equal(5.00m, project.RetentionRate); // unchanged - clamp logic was never invoked
    }

    [Theory]
    [InlineData(150)]
    [InlineData(-1)]
    public async Task Pipeline_Rejects_OutOfRange_AdvanceRate_Before_The_Handler_Ever_Runs(decimal outOfRangeRate)
    {
        var project = CreateProject();
        var repository = new FakeProjectRepository { ProjectToReturn = project };
        var command = ValidCommand(project.Id) with { AdvanceRate = outOfRangeRate };

        var result = await RunThroughPipeline(command, repository);

        Assert.True(result.IsFailure);
        Assert.Contains("AdvanceRate", result.Error);
        Assert.Equal(0, repository.FindAsyncCallCount);
        Assert.Equal(0, repository.SaveChangesCallCount);
        Assert.Equal(10.00m, project.AdvanceRate);
    }

    [Fact]
    public async Task Pipeline_Accepts_A_Fully_Valid_Command_And_The_Handler_Runs_Exactly_Once()
    {
        // Sanity companion to the two rejection tests above: the pipeline is not simply rejecting
        // everything - a valid command still reaches the handler exactly once.
        var project = CreateProject();
        var repository = new FakeProjectRepository { ProjectToReturn = project };
        var command = ValidCommand(project.Id);

        var result = await RunThroughPipeline(command, repository);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, repository.FindAsyncCallCount);
        Assert.Equal(1, repository.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handler_Clamps_OutOfRange_RetentionRate_Instead_Of_Throwing_When_The_Validator_Is_Bypassed()
    {
        // Defense-in-depth, contrasted directly with dates: if an out-of-range RetentionRate ever
        // reached the handler directly (bypassing ValidationBehavior/the validator - e.g. a future
        // caller that forgets to run the pipeline), Project.SetRetentionRate clamps it. This is
        // deliberately different from ContractFinish < ContractStart, which throws in the identical
        // bypass scenario - asserting this here pins down the actual (not assumed) behaviour so a
        // future change to either setter's semantics is caught.
        var project = CreateProject();
        var repository = new FakeProjectRepository { ProjectToReturn = project };
        var handler = new UpdateProjectCommandHandler(repository);
        var command = ValidCommand(project.Id) with { RetentionRate = 150m };

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(100m, project.RetentionRate); // clamped, not rejected/thrown
        Assert.Equal(1, repository.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handler_Clamps_OutOfRange_AdvanceRate_Instead_Of_Throwing_When_The_Validator_Is_Bypassed()
    {
        var project = CreateProject();
        var repository = new FakeProjectRepository { ProjectToReturn = project };
        var handler = new UpdateProjectCommandHandler(repository);
        var command = ValidCommand(project.Id) with { AdvanceRate = -1m };

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0m, project.AdvanceRate); // clamped, not rejected/thrown
        Assert.Equal(1, repository.SaveChangesCallCount);
    }
}
