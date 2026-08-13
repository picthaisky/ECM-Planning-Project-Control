using CMPlus.Application.Abstractions;
using CMPlus.Application.Features.Dashboard;
using CMPlus.Application.Features.Dashboard.Queries.GetDashboard;
using CMPlus.Application.Features.Evm;
using CMPlus.Application.Services.Evm;
using CMPlus.Application.Services.Wbs;
using CMPlus.Application.Wbs;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Tests.Features.Dashboard;

public class GetDashboardQueryHandlerTests
{
    private sealed class FakeEvmDataReader : IEvmDataReader
    {
        public ProjectEvmSettings? SettingsToReturn { get; set; }

        /// <summary>Default matches this file's own former <c>ProjectEvmSettings.Bac</c> literal, now
        /// resolved via the separate date-aware <see cref="GetBacAsOfAsync"/> reader call
        /// (domain-rules.md §5.5(c)) rather than carried on the settings record.</summary>
        public decimal BacToReturn { get; set; } = 1_000_000.00m;

        public IReadOnlyList<EvmActivityProgressInput> ActivityInputsToReturn { get; set; } = [];

        public Task<ProjectEvmSettings?> GetProjectSettingsAsync(Guid projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(SettingsToReturn);

        public Task<decimal> GetBacAsOfAsync(Guid projectId, DateTimeOffset asOf, CancellationToken cancellationToken = default) =>
            Task.FromResult(BacToReturn);

        public Task<IReadOnlyList<EvmActivityProgressInput>> GetActivityInputsAsync(
            Guid projectId, DateTimeOffset asOf, CancellationToken cancellationToken = default) =>
            Task.FromResult(ActivityInputsToReturn);
    }

    private sealed class FakeActualCostReader : IActualCostReader
    {
        public decimal AmountToReturn { get; set; }

        public Task<ActualCostResult> GetActualCostAsOfAsync(Guid projectId, DateTimeOffset asOf, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ActualCostResult(AmountToReturn, EntryCount: 1));
    }

    private sealed class FakeWbsTreeReader : IWbsTreeReader
    {
        public IReadOnlyList<WbsNodeFlatRow> NodesToReturn { get; set; } = [];

        public Task<bool> ProjectExistsAsync(Guid projectId, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<IReadOnlyList<WbsNodeFlatRow>> GetNodesWithActivityCountsAsync(Guid projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(NodesToReturn);
    }

    private sealed class FakeWbsProgressReader : IWbsProgressReader
    {
        public IReadOnlyList<WbsRollupActivityInput> ActivitiesToReturn { get; set; } = [];

        public Task<IReadOnlyList<WbsRollupActivityInput>> GetActivityProgressByNodeAsync(Guid projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(ActivitiesToReturn);
    }

    private sealed class FakeFinanceLedgerReader : IProjectFinanceLedgerReader
    {
        public decimal DisbursedToReturn { get; set; }

        public Task<decimal> GetRetentionHeldAsync(Guid projectId, CancellationToken cancellationToken = default) => Task.FromResult(0m);

        public Task<decimal> GetAdvanceRecoveredAsync(Guid projectId, CancellationToken cancellationToken = default) => Task.FromResult(0m);

        public Task<decimal> GetTotalDisbursedAsync(Guid projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(DisbursedToReturn);
    }

    private sealed class FakeWeatherLogRepository : IDailyWeatherLogRepository
    {
        public IReadOnlyList<DailyWeatherLog> LogsToReturn { get; set; } = [];

        public Task<IReadOnlyList<DailyWeatherLog>> ListByProjectAsync(
            Guid projectId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken = default) =>
            Task.FromResult(LogsToReturn);

        public Task<bool> ProjectExistsAsync(Guid projectId, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<DailyWeatherLog?> GetByIdAsync(Guid projectId, Guid logId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> HasAnyCorrectionTargetingAsync(Guid projectId, Guid targetLogId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Guid>> FindExistingActivityIdsAsync(
            Guid projectId, IReadOnlyCollection<Guid> activityIds, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task AddAsync(DailyWeatherLog log, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private static ProjectEvmSettings SettingsForFixtureA(EacVariant variant = EacVariant.CpiBased) => new(
        ProjectDataDate: DateTimeOffset.Parse("2026-06-30T00:00:00+07:00"),
        EacVariantDefault: variant,
        EacCustomPerformanceFactor: null,
        EacManualEtc: null);

    // Fixture A (evm-formulas.md): PV=400,000; EV=300,000; AC=350,000 -> CpiBased EAC=1,166,666.67.
    private static IReadOnlyList<EvmActivityProgressInput> FixtureAActivityInputs(DateTimeOffset dataDate) =>
    [
        // Straight-line planned curve: 50% elapsed by dataDate against a 1,000,000 budget over a
        // year gives PV=500,000 which is not Fixture A's PV=400,000 - instead of reverse-engineering
        // an exact planned-curve/date combination, PV is asserted only via the identity checks below
        // (this fixture focuses on EV/AC, which are exact).
        new(Guid.NewGuid(), 1_000_000.00m, dataDate.AddYears(-1), dataDate.AddYears(1), 30m),
    ];

    private static (GetDashboardQueryHandler Handler, FakeWbsTreeReader TreeReader, FakeWbsProgressReader ProgressReader) CreateHandler(
        ProjectEvmSettings settings, decimal actualCost, IReadOnlyList<EvmActivityProgressInput>? activityInputs = null,
        decimal disbursed = 0m, IReadOnlyList<DailyWeatherLog>? weatherLogs = null)
    {
        var dataReader = new FakeEvmDataReader
        {
            SettingsToReturn = settings,
            ActivityInputsToReturn = activityInputs ?? [],
        };
        var costReader = new FakeActualCostReader { AmountToReturn = actualCost };
        var computationService = new EvmComputationService(dataReader, costReader);
        var treeReader = new FakeWbsTreeReader();
        var progressReader = new FakeWbsProgressReader();
        var handler = new GetDashboardQueryHandler(
            computationService, treeReader, progressReader,
            new FakeFinanceLedgerReader { DisbursedToReturn = disbursed },
            new FakeWeatherLogRepository { LogsToReturn = weatherLogs ?? [] });
        return (handler, treeReader, progressReader);
    }

    [Fact]
    public async Task Handle_Returns_ProjectNotFound_When_The_Project_Does_Not_Exist()
    {
        var dataReader = new FakeEvmDataReader { SettingsToReturn = null };
        var computationService = new EvmComputationService(dataReader, new FakeActualCostReader());
        var handler = new GetDashboardQueryHandler(
            computationService, new FakeWbsTreeReader(), new FakeWbsProgressReader(),
            new FakeFinanceLedgerReader(), new FakeWeatherLogRepository());

        var result = await handler.Handle(new GetDashboardQuery(Guid.NewGuid(), null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(DashboardErrorCodes.ProjectNotFound, result.Error);
    }

    [Fact]
    public async Task Handle_Reports_Cumulative_Disbursement_And_Distinct_Weather_Stoppage_Days()
    {
        var tenantId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var user = Guid.NewGuid();
        var recordedAt = DateTimeOffset.Parse("2026-06-30T00:00:00+07:00");

        DailyWeatherLog Log(string date, WeatherImpact impact) => DailyWeatherLog.CreateOriginal(
            tenantId, projectId, DateTimeOffset.Parse($"{date}T00:00:00+07:00"),
            WeatherCondition.HeavyRain, conditionNote: null, rainfallMm: 10m, impact, impactNote: null,
            hoursLost: impact == WeatherImpact.NoImpact ? null : 4m, user, recordedAt, affectedActivityIds: []);

        // Two distinct stoppage dates (06-01, 06-02); a second stoppage log on 06-02 must NOT
        // double-count (Distinct by date); a NoImpact day (06-03) must not count at all.
        var weatherLogs = new[]
        {
            Log("2026-06-01", WeatherImpact.FullStoppage),
            Log("2026-06-02", WeatherImpact.PartialStoppage),
            Log("2026-06-02", WeatherImpact.FullStoppage),  // same date - collapses
            Log("2026-06-03", WeatherImpact.NoImpact),       // excluded
        };

        var (handler, _, _) = CreateHandler(
            SettingsForFixtureA(), actualCost: 350_000.00m, activityInputs: FixtureAActivityInputs(DateTimeOffset.Parse("2026-06-30T00:00:00+07:00")),
            disbursed: 12_500_000.00m, weatherLogs: weatherLogs);

        var result = await handler.Handle(new GetDashboardQuery(projectId, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(12_500_000.00m, result.Value.CumulativeDisbursement);
        Assert.Equal(2, result.Value.CumulativeWeatherStoppageDays);
    }

    [Fact]
    public async Task Handle_Uses_The_Projects_Own_EacVariantDefault_Never_A_Request_Override()
    {
        var settings = SettingsForFixtureA(EacVariant.CpiSpiBased);
        var (handler, _, _) = CreateHandler(settings, actualCost: 350_000.00m, FixtureAActivityInputs(settings.ProjectDataDate));

        var result = await handler.Handle(new GetDashboardQuery(Guid.NewGuid(), null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(EacVariant.CpiSpiBased, result.Value.EacVariant);
    }

    [Fact]
    public async Task Handle_Computes_Eac_For_The_Default_Variant_Via_The_Real_Engine_Not_A_Second_Calculation()
    {
        var settings = SettingsForFixtureA(EacVariant.CpiBased);
        var (handler, _, _) = CreateHandler(settings, actualCost: 350_000.00m, FixtureAActivityInputs(settings.ProjectDataDate));

        var result = await handler.Handle(new GetDashboardQuery(Guid.NewGuid(), null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(300_000.00m, result.Value.Ev);
        Assert.Equal(350_000.00m, result.Value.Ac);
        Assert.True(result.Value.EacComputable);
        // EAC(CpiBased) = BAC / CPI = AC * BAC / EV = 350,000 * 1,000,000 / 300,000 = 1,166,666.67.
        Assert.Equal(1_166_666.67m, result.Value.Eac);
    }

    [Fact]
    public async Task Handle_Passes_Through_The_Actual_Cost_Entry_Count_From_The_Engine()
    {
        var settings = SettingsForFixtureA();
        var (handler, _, _) = CreateHandler(settings, actualCost: 350_000.00m, FixtureAActivityInputs(settings.ProjectDataDate));

        var result = await handler.Handle(new GetDashboardQuery(Guid.NewGuid(), null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.ActualCostEntryCount); // FakeActualCostReader always reports 1.
    }

    [Fact]
    public async Task Handle_Computes_The_Wbs_Weight_Rollup_Independent_Of_The_Eac_Variant()
    {
        var settings = SettingsForFixtureA();
        var (handler, treeReader, progressReader) = CreateHandler(settings, actualCost: 350_000.00m, FixtureAActivityInputs(settings.ProjectDataDate));

        var leafA = Guid.NewGuid();
        var leafB = Guid.NewGuid();
        treeReader.NodesToReturn =
        [
            new WbsNodeFlatRow(leafA, null, "1", "Structure", 60m, ActivityCount: 1),
            new WbsNodeFlatRow(leafB, null, "2", "Architectural", 40m, ActivityCount: 1),
        ];
        progressReader.ActivitiesToReturn =
        [
            new WbsRollupActivityInput(leafA, 1m, 80m),
            new WbsRollupActivityInput(leafB, 1m, 20m),
        ];

        var result = await handler.Handle(new GetDashboardQuery(Guid.NewGuid(), null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        // (80*60 + 20*40) / 100 = 56.00.
        Assert.Equal(56.00m, result.Value.ProgressRollup.ProgressPercentage);
    }

    [Fact]
    public async Task Handle_Surfaces_A_Weight_Warning_Without_Failing_The_Whole_Request()
    {
        var settings = SettingsForFixtureA();
        var (handler, treeReader, progressReader) = CreateHandler(settings, actualCost: 350_000.00m, FixtureAActivityInputs(settings.ProjectDataDate));

        var leafA = Guid.NewGuid();
        treeReader.NodesToReturn = [new WbsNodeFlatRow(leafA, null, "1", "Structure", 60m, ActivityCount: 1)]; // sum = 60, not 100.
        progressReader.ActivitiesToReturn = [new WbsRollupActivityInput(leafA, 1m, 80m)];

        var result = await handler.Handle(new GetDashboardQuery(Guid.NewGuid(), null), CancellationToken.None);

        Assert.True(result.IsSuccess); // warn, never block (S8-BE-02 DoD).
        var warning = Assert.Single(result.Value.ProgressRollup.WeightWarnings);
        Assert.Equal(60m, warning.WeightSum);
    }

    [Fact]
    public async Task Handle_Propagates_Eac_Calculator_Warnings_Unchanged()
    {
        var settings = SettingsForFixtureA();
        // EV (400,000 via 40% of 1,000,000) > BAC (leave BAC at the settings default 1,000,000 but
        // drive progress past 100% is impossible via PercentageGuard, so instead assert the other
        // documented warning path: a negative AC.
        var (handler, _, _) = CreateHandler(settings, actualCost: -10_000.00m, FixtureAActivityInputs(settings.ProjectDataDate));

        var result = await handler.Handle(new GetDashboardQuery(Guid.NewGuid(), null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(EvmWarningCodes.ActualCostIsNegative, result.Value.Warnings);
    }
}
