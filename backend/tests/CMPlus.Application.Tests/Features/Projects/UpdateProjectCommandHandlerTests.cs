using CMPlus.Application.Abstractions;
using CMPlus.Application.Features.Projects;
using CMPlus.Application.Features.Projects.Commands.UpdateProject;
using CMPlus.Domain.Common;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Tests.Features.Projects;

public class UpdateProjectCommandHandlerTests
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
        Guid.NewGuid(), "Original Name", "P-001", "Original Owner",
        DateTimeOffset.Parse("2026-01-01T00:00:00Z"), DateTimeOffset.Parse("2026-12-31T00:00:00Z"),
        bac: 1_000_000m, dataDate: DateTimeOffset.Parse("2026-06-01T00:00:00Z"));

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
        AdvanceAmountPaid: 145_000m,
        AdvanceRecoveryMethod: AdvanceRecoveryMethod.ProRata,
        AdvanceRecoveryStartPct: null,
        AdvanceRecoveryRatePct: null,
        AdvanceRecoveryEndPct: null);

    [Fact]
    public async Task Handle_Returns_NotFound_When_The_Project_Does_Not_Exist()
    {
        var repository = new FakeProjectRepository { ProjectToReturn = null };
        var handler = new UpdateProjectCommandHandler(repository);

        var result = await handler.Handle(ValidCommand(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ProjectErrorCodes.NotFound, result.Error);
        Assert.Equal(0, repository.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_Applies_Every_Editable_Field_And_Saves_Once()
    {
        var project = CreateProject();
        var repository = new FakeProjectRepository { ProjectToReturn = project };
        var handler = new UpdateProjectCommandHandler(repository);
        var command = ValidCommand(project.Id);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, repository.SaveChangesCallCount);

        Assert.Equal(command.Name, project.Name);
        Assert.Equal(command.Code, project.Code);
        Assert.Equal(command.Owner, project.Owner);
        Assert.Equal(command.ContractStart, project.ContractStart);
        Assert.Equal(command.ContractFinish, project.ContractFinish);
        Assert.Equal(command.Bac, project.BAC);
        Assert.Equal(command.ContractValue, project.ContractValue);
        Assert.Equal(command.RetentionRate, project.RetentionRate);
        Assert.Equal(command.AdvanceRate, project.AdvanceRate);
        Assert.Equal(command.RetentionCapPercentage, project.RetentionCapPercentage);
        Assert.Equal(command.DefectsLiabilityMonths, project.DefectsLiabilityMonths);
        Assert.Equal(command.AdvanceAmountPaid, project.AdvanceAmountPaid);
        Assert.Equal(command.AdvanceRecoveryMethod, project.AdvanceRecoveryMethod);

        Assert.Equal(project.Id, result.Value.Id);
        Assert.Equal(command.Name, result.Value.Name);
        Assert.Equal(command.RetentionRate, result.Value.RetentionRate);
    }

    [Fact]
    public async Task Handle_Propagates_The_Domain_Invariant_When_ContractFinish_Precedes_ContractStart()
    {
        // Defense-in-depth: even if a ContractFinish < ContractStart command somehow reached the
        // handler (bypassing UpdateProjectCommandValidator), Project.SetContractDates itself
        // rejects it - the DomainException propagates to become a 400 domain-rule-violation
        // (GlobalExceptionHandler), never a silently-accepted invalid date range (US-4.3 DoD).
        var project = CreateProject();
        var repository = new FakeProjectRepository { ProjectToReturn = project };
        var handler = new UpdateProjectCommandHandler(repository);
        var command = ValidCommand(project.Id) with
        {
            ContractStart = DateTimeOffset.Parse("2026-06-01T00:00:00Z"),
            ContractFinish = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
        };

        await Assert.ThrowsAsync<DomainException>(() => handler.Handle(command, CancellationToken.None));
        Assert.Equal(0, repository.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_Never_Reaches_Any_Aggregate_Other_Than_Project()
    {
        // US-4.4 DoD: "changing a rate must not retroactively recalculate any already-issued
        // Payment Certificate." IProjectRepository exposes only Project - there is no way for this
        // handler to reach a PaymentCertificate (or any other aggregate) even if it wanted to, so
        // the invariant holds by construction, not merely by convention. This test documents that
        // guarantee at the command level, per the S4-BE-02 DoD, since no PaymentCertificate entity
        // exists yet to assert against directly (Sprint 9).
        var project = CreateProject();
        var repository = new FakeProjectRepository { ProjectToReturn = project };
        var handler = new UpdateProjectCommandHandler(repository);

        var result = await handler.Handle(ValidCommand(project.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        // IProjectRepository has exactly two members (FindAsync, TrySaveChangesAsync) - both already
        // exercised above - and neither can reach any other repository/aggregate.
    }

    /// <summary>
    /// ADR-0017. The Domain guard on <c>Project.SetBac</c> is defense in depth, but on its own it
    /// surfaces as <c>GlobalExceptionHandler</c>'s generic 400 "The request violates a domain rule",
    /// which tells a PM nothing about why their budget edit was rejected. The handler must return the
    /// specific code so the caller learns the budget is derived and what to do instead.
    /// </summary>
    [Fact]
    public async Task Editing_Bac_Is_Refused_With_A_Specific_Code_Once_A_Vo_Has_Been_Approved()
    {
        var project = CreateProject();
        project.ApplyVariationOrderApproval(250_000.00m, DateTimeOffset.UtcNow);
        var repository = new FakeProjectRepository { ProjectToReturn = project };
        var handler = new UpdateProjectCommandHandler(repository);

        // ValidCommand carries a Bac that differs from the project's current value.
        var result = await handler.Handle(ValidCommand(project.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ProjectErrorCodes.BacLockedByApprovedVariationOrders, result.Error);
        Assert.Equal(0, repository.SaveChangesCallCount); // nothing partially applied
    }

    /// <summary>
    /// The lock must not make the whole project un-editable: `UpdateProjectCommand` is a
    /// full-representation PUT, so an unchanged BAC arrives on every edit of any other field.
    /// </summary>
    [Fact]
    public async Task Editing_Other_Fields_Still_Works_After_A_Vo_When_Bac_Is_Resubmitted_Unchanged()
    {
        var project = CreateProject();
        project.ApplyVariationOrderApproval(250_000.00m, DateTimeOffset.UtcNow);
        var repository = new FakeProjectRepository { ProjectToReturn = project };
        var handler = new UpdateProjectCommandHandler(repository);
        var command = ValidCommand(project.Id) with { Bac = project.BAC };

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Updated Name", project.Name);
        Assert.Equal(1, repository.SaveChangesCallCount);
    }

    /// <summary>
    /// Reachable only since <c>Project.RowVersion</c> was added for S10-SEC-01 finding H-03. Before
    /// the token existed a concurrent edit silently lost one write; the naive fix would have let the
    /// resulting <c>DbUpdateConcurrencyException</c> escape as a 500. Neither is acceptable on an
    /// ordinary project edit, so the conflict must surface as a mapped 409.
    /// </summary>
    [Fact]
    public async Task A_Concurrent_Edit_Returns_A_Conflict_Result_Rather_Than_Throwing_Or_Silently_Losing_The_Write()
    {
        var project = CreateProject();
        var repository = new FakeProjectRepository
        {
            ProjectToReturn = project,
            NextSaveHitsConcurrencyConflict = true,
        };
        var handler = new UpdateProjectCommandHandler(repository);

        var result = await handler.Handle(ValidCommand(project.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ProjectErrorCodes.ConcurrencyConflict, result.Error);
        Assert.Equal(1, repository.SaveChangesCallCount);
    }
}
