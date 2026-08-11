using CMPlus.Application.Features.Baseline;
using CMPlus.Application.Features.Baseline.Commands.ActivateBaseline;

namespace CMPlus.Application.Tests.Features.Baseline;

public class ActivateBaselineCommandHandlerTests
{
    private static Domain.Entities.Baseline Seed(FakeBaselineRepository repository, Guid projectId, string name, bool active = false)
    {
        var baseline = Domain.Entities.Baseline.Capture(
            Guid.NewGuid(), projectId, name, DateTimeOffset.UtcNow, Guid.NewGuid(), 1_000_000.00m, []);
        if (active)
        {
            baseline.Activate();
        }

        repository.BaselinesById[baseline.Id] = baseline;
        return baseline;
    }

    [Fact]
    public async Task Handle_Returns_NotFound_When_The_Baseline_Does_Not_Exist()
    {
        var repository = new FakeBaselineRepository();
        var handler = new ActivateBaselineCommandHandler(repository);

        var result = await handler.Handle(new ActivateBaselineCommand(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(BaselineErrorCodes.NotFound, result.Error);
        Assert.Equal(0, repository.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_Returns_NotFound_When_The_Baseline_Belongs_To_A_Different_Project()
    {
        // Cross-project (like cross-tenant) is indistinguishable from "does not exist" - bare 404,
        // never a 422/403 that would confirm the id exists somewhere else.
        var repository = new FakeBaselineRepository();
        var actualProjectId = Guid.NewGuid();
        var otherProjectId = Guid.NewGuid();
        var baseline = Seed(repository, actualProjectId, "BL-1");
        var handler = new ActivateBaselineCommandHandler(repository);

        var result = await handler.Handle(new ActivateBaselineCommand(otherProjectId, baseline.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(BaselineErrorCodes.NotFound, result.Error);
        Assert.Equal(0, repository.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_Activates_The_Target_And_Deactivates_The_Previously_Active_Baseline()
    {
        var repository = new FakeBaselineRepository();
        var projectId = Guid.NewGuid();
        var original = Seed(repository, projectId, "BL-0 (Original)", active: true);
        var revised = Seed(repository, projectId, "BL-1 (Revised)");
        var handler = new ActivateBaselineCommandHandler(repository);

        var result = await handler.Handle(new ActivateBaselineCommand(projectId, revised.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsActive);
        Assert.True(revised.IsActive);
        Assert.False(original.IsActive);
        // Two SEPARATE saves (S14-BE-01 fix) - one for the deactivate, one for the activate - never
        // one batched save covering both. See FakeBaselineRepository.TryActivateAsync's remarks.
        Assert.Equal(2, repository.SaveChangesCallCount);
    }

    /// <summary>
    /// The mutation evidence this task explicitly asks for: "two simultaneously-active baselines
    /// are impossible" - proven here at the <b>application layer</b> (this environment's EF Core
    /// InMemory provider does not enforce the DB-level filtered unique index at all, so that half
    /// is not provable by any test that can run here - see <c>ActivateBaselineCommandHandler</c>'s
    /// remarks). Activates three baselines for the same project in turn and asserts, after each
    /// activation, that exactly one is active - never zero, never two.
    /// </summary>
    [Fact]
    public async Task Handle_Never_Leaves_More_Than_One_Baseline_Active_For_A_Project_Across_Repeated_Activations()
    {
        var repository = new FakeBaselineRepository();
        var projectId = Guid.NewGuid();
        var blOriginal = Seed(repository, projectId, "BL-0", active: true);
        var blRevised = Seed(repository, projectId, "BL-1");
        var blWhatIf = Seed(repository, projectId, "BL-2");
        var handler = new ActivateBaselineCommandHandler(repository);

        Assert.Equal(1, ActiveCountFor(repository, projectId));

        await handler.Handle(new ActivateBaselineCommand(projectId, blRevised.Id), CancellationToken.None);
        Assert.Equal(1, ActiveCountFor(repository, projectId));
        Assert.True(blRevised.IsActive);
        Assert.False(blOriginal.IsActive);
        Assert.False(blWhatIf.IsActive);

        await handler.Handle(new ActivateBaselineCommand(projectId, blWhatIf.Id), CancellationToken.None);
        Assert.Equal(1, ActiveCountFor(repository, projectId));
        Assert.True(blWhatIf.IsActive);
        Assert.False(blRevised.IsActive);
        Assert.False(blOriginal.IsActive);

        // Switching back to the very first baseline must work identically - not a one-way door.
        await handler.Handle(new ActivateBaselineCommand(projectId, blOriginal.Id), CancellationToken.None);
        Assert.Equal(1, ActiveCountFor(repository, projectId));
        Assert.True(blOriginal.IsActive);
        Assert.False(blRevised.IsActive);
        Assert.False(blWhatIf.IsActive);
    }

    private static int ActiveCountFor(FakeBaselineRepository repository, Guid projectId) =>
        repository.BaselinesById.Values.Count(b => b.ProjectId == projectId && b.IsActive);

    [Fact]
    public async Task Handle_Is_A_No_Op_When_The_Target_Is_Already_Active()
    {
        var repository = new FakeBaselineRepository();
        var projectId = Guid.NewGuid();
        var baseline = Seed(repository, projectId, "BL-1", active: true);
        var handler = new ActivateBaselineCommandHandler(repository);

        var result = await handler.Handle(new ActivateBaselineCommand(projectId, baseline.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsActive);
        // No write issued at all - Baseline.Activate's own remarks note this would be safe either
        // way (EF's change tracker would no-op an unchanged value), but the handler skips the read
        // and the write entirely as a deliberate optimisation.
        Assert.Equal(0, repository.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_Returns_ConcurrencyConflict_When_The_Save_Fails()
    {
        var repository = new FakeBaselineRepository { NextSaveHitsConcurrencyConflict = true };
        var projectId = Guid.NewGuid();
        var baseline = Seed(repository, projectId, "BL-1");
        var handler = new ActivateBaselineCommandHandler(repository);

        var result = await handler.Handle(new ActivateBaselineCommand(projectId, baseline.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(BaselineErrorCodes.ConcurrencyConflict, result.Error);
    }

    [Fact]
    public async Task Handle_Activating_The_First_Ever_Baseline_For_A_Project_Needs_No_Previous_Deactivation()
    {
        // The "second activation" ordering defect (S14-BE-01, docs/perf/s14-baseline-storage.md §2)
        // does not even arise on the very first activation - there is nothing to deactivate, so only
        // one save runs regardless of the two-phase fix. Pinned explicitly so a future change cannot
        // silently assume a previous active row always exists.
        var repository = new FakeBaselineRepository();
        var projectId = Guid.NewGuid();
        var baseline = Seed(repository, projectId, "BL-0");
        var handler = new ActivateBaselineCommandHandler(repository);

        var result = await handler.Handle(new ActivateBaselineCommand(projectId, baseline.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(baseline.IsActive);
        Assert.Equal(1, repository.SaveChangesCallCount);
    }
}
