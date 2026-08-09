using CMPlus.Application.Abstractions;
using CMPlus.Application.Features.Evm;
using CMPlus.Application.Features.Evm.Commands.CloseEvmPeriod;
using CMPlus.Application.Services.Evm;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Tests.Features.Evm;

public class CloseEvmPeriodCommandHandlerTests
{
    private sealed class FakeEvmDataReader : IEvmDataReader
    {
        public ProjectEvmSettings? SettingsToReturn { get; set; }
        public IReadOnlyList<EvmActivityProgressInput> ActivityInputsToReturn { get; set; } = [];

        public Task<ProjectEvmSettings?> GetProjectSettingsAsync(Guid projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(SettingsToReturn);

        public Task<IReadOnlyList<EvmActivityProgressInput>> GetActivityInputsAsync(
            Guid projectId, DateTimeOffset asOf, CancellationToken cancellationToken = default) =>
            Task.FromResult(ActivityInputsToReturn);
    }

    private sealed class FakeActualCostReader(decimal actualCost) : IActualCostReader
    {
        public Task<ActualCostResult> GetActualCostAsOfAsync(Guid projectId, DateTimeOffset asOf, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ActualCostResult(actualCost, actualCost == 0m ? 0 : 1));
    }

    private sealed class FakeEvmSnapshotRepository : IEvmSnapshotRepository
    {
        public bool ExistsForDate { get; set; }
        public EvmPeriodSnapshot? AddedSnapshot { get; private set; }
        public int AddCallCount { get; private set; }

        public Task<bool> ExistsForDataDateAsync(Guid projectId, DateTimeOffset dataDate, CancellationToken cancellationToken = default) =>
            Task.FromResult(ExistsForDate);

        public Task AddAsync(EvmPeriodSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            AddedSnapshot = snapshot;
            AddCallCount++;
            return Task.CompletedTask;
        }

        /// <summary>Not exercised by the close-period tests in this file (the listing read has its
        /// own handler tests) - returns whatever single snapshot was added so the fake stays
        /// self-consistent rather than lying with a permanently empty list.</summary>
        public Task<IReadOnlyList<EvmPeriodSnapshot>> ListAsync(
            Guid projectId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EvmPeriodSnapshot>>(
                AddedSnapshot is null ? [] : [AddedSnapshot]);
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

    private static ProjectEvmSettings DefaultSettings(EacVariant defaultVariant = EacVariant.CpiBased) => new(
        Bac: 1_000_000.00m,
        ProjectDataDate: DateTimeOffset.Parse("2026-06-30T00:00:00+07:00"),
        EacVariantDefault: defaultVariant,
        EacCustomPerformanceFactor: null,
        EacManualEtc: null);

    private static (CloseEvmPeriodCommandHandler Handler, FakeEvmSnapshotRepository Repository) CreateHandler(
        ProjectEvmSettings? settings,
        IReadOnlyList<EvmActivityProgressInput>? activityInputs = null,
        decimal actualCost = 350_000.00m,
        bool existsForDate = false,
        Guid? tenantId = null,
        Guid? userId = null,
        DateTimeOffset? now = null)
    {
        var dataReader = new FakeEvmDataReader { SettingsToReturn = settings, ActivityInputsToReturn = activityInputs ?? [] };
        var costReader = new FakeActualCostReader(actualCost);
        var snapshotRepository = new FakeEvmSnapshotRepository { ExistsForDate = existsForDate };
        var computationService = new EvmComputationService(dataReader, costReader);

        var handler = new CloseEvmPeriodCommandHandler(
            computationService,
            snapshotRepository,
            new FakeTenantProvider(tenantId ?? Guid.NewGuid()),
            new FakeCurrentUserContext(userId ?? Guid.NewGuid()),
            new FakeClock(now ?? DateTimeOffset.UtcNow));

        return (handler, snapshotRepository);
    }

    [Fact]
    public async Task Handle_Returns_ProjectNotFound_When_The_Project_Does_Not_Exist()
    {
        var (handler, repository) = CreateHandler(settings: null);

        var result = await handler.Handle(
            new CloseEvmPeriodCommand(Guid.NewGuid(), DateTimeOffset.Parse("2026-06-30T00:00:00Z")), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(EvmErrorCodes.ProjectNotFound, result.Error);
        Assert.Equal(0, repository.AddCallCount);
    }

    [Fact]
    public async Task Handle_Returns_Conflict_When_A_Snapshot_For_This_Data_Date_Already_Exists()
    {
        var (handler, repository) = CreateHandler(DefaultSettings(), existsForDate: true);

        var result = await handler.Handle(
            new CloseEvmPeriodCommand(Guid.NewGuid(), DateTimeOffset.Parse("2026-06-30T00:00:00Z")), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(EvmErrorCodes.SnapshotAlreadyExists, result.Error);
        Assert.Equal(0, repository.AddCallCount);
    }

    [Fact]
    public async Task Handle_Freezes_The_Live_Computation_Using_The_Projects_Own_Default_Variant()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var dataDate = DateTimeOffset.Parse("2026-06-30T00:00:00+07:00");
        var projectId = Guid.NewGuid();

        var activityInputs = new[]
        {
            new EvmActivityProgressInput(Guid.NewGuid(), 1_000_000.00m, dataDate.AddMonths(-6), dataDate.AddMonths(6), 30m),
        };

        var (handler, repository) = CreateHandler(
            DefaultSettings(defaultVariant: EacVariant.Atypical),
            activityInputs,
            actualCost: 350_000.00m,
            tenantId: tenantId,
            userId: userId,
            now: DateTimeOffset.Parse("2026-07-01T00:00:00Z"));

        var result = await handler.Handle(new CloseEvmPeriodCommand(projectId, dataDate), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, repository.AddCallCount);

        var snapshot = repository.AddedSnapshot!;
        Assert.Equal(tenantId, snapshot.TenantId);
        Assert.Equal(projectId, snapshot.ProjectId);
        Assert.Equal(dataDate, snapshot.DataDate);
        Assert.Equal(1_000_000.00m, snapshot.Bac);
        Assert.Equal(300_000.00m, snapshot.Ev); // 1,000,000 * 30%.
        Assert.Equal(350_000.00m, snapshot.Ac);
        Assert.Equal(EacVariant.Atypical, snapshot.EacVariant); // the project's own default, frozen.
        Assert.Equal(userId, snapshot.CreatedByUserId);
        Assert.Equal(DateTimeOffset.Parse("2026-07-01T00:00:00Z"), snapshot.CreatedAt);

        // Atypical: PF = 1; ETC = BAC - EV = 700,000.00; EAC = AC + ETC = 1,050,000.00.
        Assert.Equal(1m, snapshot.PerformanceFactor);
        Assert.Equal(700_000.00m, snapshot.Etc);
        Assert.Equal(1_050_000.00m, snapshot.Eac);

        Assert.Equal(result.Value.SnapshotId, snapshot.Id);
        Assert.Equal(EacVariant.Atypical, result.Value.EacVariant);
    }

    [Fact]
    public async Task Handle_Freezes_Nulls_Honestly_When_The_Default_Variant_Is_Not_Computable()
    {
        // A not-started project (PV=EV=AC=0) - every variant is null, reason NotStarted. The
        // snapshot must freeze that honestly, never a fabricated 0.00.
        var (handler, repository) = CreateHandler(
            DefaultSettings(defaultVariant: EacVariant.CpiBased) with { Bac = 0m },
            activityInputs: [],
            actualCost: 0m);

        var result = await handler.Handle(
            new CloseEvmPeriodCommand(Guid.NewGuid(), DateTimeOffset.Parse("2026-06-30T00:00:00Z")), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var snapshot = repository.AddedSnapshot!;
        Assert.Null(snapshot.PerformanceFactor);
        Assert.Null(snapshot.Eac);
        Assert.Null(snapshot.Etc);
        Assert.Null(snapshot.Vac);
    }
}
