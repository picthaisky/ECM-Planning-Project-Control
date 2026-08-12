using CMPlus.Application.Abstractions;
using CMPlus.Application.Features.ActualCosts;
using CMPlus.Application.Features.ActualCosts.Commands.RecordActualCost;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Tests.Features.ActualCosts;

public class RecordActualCostCommandHandlerTests
{
    private sealed class FakeActualCostRepository : IActualCostRepository
    {
        public bool ProjectExists { get; set; } = true;
        public bool WbsNodeExists { get; set; } = true;
        public bool ActivityExists { get; set; } = true;
        public bool EntryExists { get; set; } = true;
        public bool ClosedPeriodExists { get; set; }
        public ActualCostEntry? AddedEntry { get; private set; }
        public int AddCallCount { get; private set; }

        public Task<bool> ProjectExistsAsync(Guid projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(ProjectExists);

        public Task<bool> WbsNodeExistsAsync(Guid projectId, Guid wbsNodeId, CancellationToken cancellationToken = default) =>
            Task.FromResult(WbsNodeExists);

        public Task<bool> ActivityExistsAsync(Guid projectId, Guid activityId, CancellationToken cancellationToken = default) =>
            Task.FromResult(ActivityExists);

        public Task<bool> EntryExistsAsync(Guid projectId, Guid entryId, CancellationToken cancellationToken = default) =>
            Task.FromResult(EntryExists);

        public Task<bool> ClosedPeriodExistsOnOrAfterAsync(Guid projectId, DateTimeOffset incurredDate, CancellationToken cancellationToken = default) =>
            Task.FromResult(ClosedPeriodExists);

        public Task AddAsync(ActualCostEntry entry, CancellationToken cancellationToken = default)
        {
            AddedEntry = entry;
            AddCallCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeTenantProvider(Guid tenantId) : ITenantProvider
    {
        public Guid TenantId { get; } = tenantId;
    }

    private sealed class FakeCurrentUserContext(Guid? userId) : ICurrentUserContext
    {
        public Guid? UserId { get; } = userId;

        public UserRole Role => UserRole.PM;
    }

    private sealed class FakeClock(DateTimeOffset now) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private static RecordActualCostCommand DefaultCommand(
        Guid? projectId = null,
        Guid? wbsNodeId = null,
        Guid? activityId = null,
        Guid? reversesEntryId = null,
        decimal amount = 1_000_000.00m,
        string? note = null,
        DateTimeOffset? incurredDate = null) => new(
        projectId ?? Guid.NewGuid(),
        wbsNodeId,
        activityId,
        CostCategory.Subcontract,
        ActualCostEntryType.Actual,
        amount,
        incurredDate ?? DateTimeOffset.Parse("2026-01-31T00:00:00+07:00"),
        reversesEntryId,
        "INV-2026-0001",
        "5100-100",
        "ABC Subcontractor Co., Ltd.",
        note,
        PaidDate: null,
        Quantity: null,
        UnitOfMeasure: null);

    private static (RecordActualCostCommandHandler Handler, FakeActualCostRepository Repository) CreateHandler(
        Guid? tenantId = null, Guid? userId = null, DateTimeOffset? now = null)
    {
        var repository = new FakeActualCostRepository();
        var handler = new RecordActualCostCommandHandler(
            repository,
            new FakeTenantProvider(tenantId ?? Guid.NewGuid()),
            new FakeCurrentUserContext(userId ?? Guid.NewGuid()),
            new FakeClock(now ?? DateTimeOffset.UtcNow));

        return (handler, repository);
    }

    [Fact]
    public async Task Handle_Returns_ProjectNotFound_When_The_Project_Does_Not_Exist()
    {
        var (handler, repository) = CreateHandler();
        repository.ProjectExists = false;

        var result = await handler.Handle(DefaultCommand(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ActualCostErrorCodes.ProjectNotFound, result.Error);
        Assert.Equal(0, repository.AddCallCount);
    }

    [Fact]
    public async Task Handle_Returns_WbsNodeNotFound_When_The_Supplied_Node_Does_Not_Belong_To_The_Project()
    {
        var (handler, repository) = CreateHandler();
        repository.WbsNodeExists = false;

        var result = await handler.Handle(DefaultCommand(wbsNodeId: Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ActualCostErrorCodes.WbsNodeNotFound, result.Error);
        Assert.Equal(0, repository.AddCallCount);
    }

    [Fact]
    public async Task Handle_Returns_ActivityNotFound_When_The_Supplied_Activity_Does_Not_Belong_To_The_Project()
    {
        var (handler, repository) = CreateHandler();
        repository.ActivityExists = false;

        var result = await handler.Handle(DefaultCommand(activityId: Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ActualCostErrorCodes.ActivityNotFound, result.Error);
        Assert.Equal(0, repository.AddCallCount);
    }

    [Fact]
    public async Task Handle_Returns_ReversedEntryNotFound_When_ReversesEntryId_Does_Not_Exist_In_This_Project()
    {
        var (handler, repository) = CreateHandler();
        repository.EntryExists = false;

        var result = await handler.Handle(DefaultCommand(reversesEntryId: Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ActualCostErrorCodes.ReversedEntryNotFound, result.Error);
        Assert.Equal(0, repository.AddCallCount);
    }

    [Fact]
    public async Task Handle_Requires_A_Note_When_Posting_Into_An_Already_Closed_Period()
    {
        // actual-cost.md §6.6: allowed, but requires a Note.
        var (handler, repository) = CreateHandler();
        repository.ClosedPeriodExists = true;

        var result = await handler.Handle(DefaultCommand(note: null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ActualCostErrorCodes.NoteRequiredForClosedPeriod, result.Error);
        Assert.Equal(0, repository.AddCallCount);
    }

    [Fact]
    public async Task Handle_Succeeds_Posting_Into_A_Closed_Period_When_A_Note_Is_Supplied()
    {
        var (handler, repository) = CreateHandler();
        repository.ClosedPeriodExists = true;

        var result = await handler.Handle(DefaultCommand(note: "Late invoice for March - restates a closed period."), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, repository.AddCallCount);
    }

    [Fact]
    public async Task Handle_Persists_An_Entry_Stamped_With_Server_Side_Tenant_PostedAt_And_PostedBy()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var now = DateTimeOffset.Parse("2026-04-02T00:00:00+07:00");
        var projectId = Guid.NewGuid();
        var (handler, repository) = CreateHandler(tenantId, userId, now);

        var result = await handler.Handle(DefaultCommand(projectId: projectId, amount: 640_000.00m), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var entry = repository.AddedEntry!;
        Assert.Equal(tenantId, entry.TenantId);
        Assert.Equal(projectId, entry.ProjectId);
        Assert.Equal(640_000.00m, entry.Amount);
        Assert.Equal(now, entry.PostedAt);
        Assert.Equal(userId, entry.PostedByUserId);

        // Source is always ManualEntry for this write path - never trusted from the request
        // (RecordActualCostCommand deliberately has no Source field at all).
        Assert.Equal(ActualCostSource.ManualEntry, entry.Source);
        Assert.Null(entry.FileImportJobId);

        Assert.Equal(result.Value.Id, entry.Id);
        Assert.Equal(result.Value.Amount, entry.Amount);
    }

    [Fact]
    public async Task Handle_Allows_A_Negative_Amount_For_A_Reversal_Or_Adjustment()
    {
        var (handler, repository) = CreateHandler();

        var result = await handler.Handle(DefaultCommand(amount: -600_000.00m), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(-600_000.00m, repository.AddedEntry!.Amount);
    }

    [Fact]
    public async Task Handle_Fails_Closed_With_ActorRequired_On_A_Null_UserId_Never_Fabricates_Guid_Empty()
    {
        // Security review sprint-15.md L-01b - the central assertion is the SECOND one: no entry is
        // ever added with a fabricated actor. Constructed directly (not via CreateHandler) so the
        // explicit `null` cannot be masked by a "generate one if absent" fallback.
        var repository = new FakeActualCostRepository();
        var handler = new RecordActualCostCommandHandler(
            repository, new FakeTenantProvider(Guid.NewGuid()), new FakeCurrentUserContext(null), new FakeClock(DateTimeOffset.UtcNow));

        var result = await handler.Handle(DefaultCommand(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ActualCostErrorCodes.ActorRequired, result.Error);
        Assert.Equal(0, repository.AddCallCount);
        Assert.Null(repository.AddedEntry);
    }
}
