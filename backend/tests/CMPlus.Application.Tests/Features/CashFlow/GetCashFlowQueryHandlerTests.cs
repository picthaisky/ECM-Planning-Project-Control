using CMPlus.Application.Abstractions;
using CMPlus.Application.Features.CashFlow;
using CMPlus.Application.Features.CashFlow.Queries.GetCashFlow;
using CMPlus.Application.Features.Evm;
using CMPlus.Application.Services.Evm;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Tests.Features.CashFlow;

/// <summary>
/// S8-BE-01. Proves the "no calculation outside the EVM engine" rule holds structurally: every fake
/// here only ever supplies raw inputs to the real <see cref="EvmEngine"/>/<see cref="EacCalculator"/>
/// (via the real, non-faked <see cref="EvmComputationService"/>) or plays back pre-baked
/// <see cref="EvmPeriodSnapshot"/> rows - the handler itself is never handed a pre-computed PV/EV/AC
/// figure to simply pass through, so a test that only checked "the numbers match" could not
/// distinguish a correct implementation from one that duplicated the summation. These tests instead
/// assert the *subtraction identities* (period deltas sum back to the cumulative total; the last
/// bucket's cumulative equals the top-level cumulative) that only hold if the handler is doing plain
/// arithmetic on already-engine-produced values.
/// </summary>
public class GetCashFlowQueryHandlerTests
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

    private sealed class FakeActualCostReader : IActualCostReader
    {
        /// <summary>Keyed by date so a test can make AC(t) vary across the multiple calls one
        /// request can trigger (baseline/live) without an interpolation-dependent activity curve.</summary>
        public Dictionary<DateTimeOffset, decimal> AmountByDate { get; } = [];
        public decimal DefaultAmount { get; set; }

        public Task<ActualCostResult> GetActualCostAsOfAsync(Guid projectId, DateTimeOffset asOf, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ActualCostResult(
                AmountByDate.TryGetValue(asOf, out var amount) ? amount : DefaultAmount, EntryCount: 1));
    }

    private sealed class FakeEvmSnapshotRepository : IEvmSnapshotRepository
    {
        public List<EvmPeriodSnapshot> Snapshots { get; } = [];

        public Task<bool> ExistsForDataDateAsync(Guid projectId, DateTimeOffset dataDate, CancellationToken cancellationToken = default) =>
            Task.FromResult(Snapshots.Any(s => s.DataDate == dataDate));

        public Task AddAsync(EvmPeriodSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            Snapshots.Add(snapshot);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<EvmPeriodSnapshot>> ListAsync(
            Guid projectId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken = default)
        {
            IEnumerable<EvmPeriodSnapshot> query = Snapshots.OrderBy(s => s.DataDate);
            if (from is not null)
            {
                query = query.Where(s => s.DataDate >= from.Value);
            }

            if (to is not null)
            {
                query = query.Where(s => s.DataDate <= to.Value);
            }

            return Task.FromResult<IReadOnlyList<EvmPeriodSnapshot>>(query.ToList());
        }
    }

    private static EvmPeriodSnapshot Snapshot(DateTimeOffset dataDate, decimal bac, decimal pv, decimal ev, decimal ac) => new(
        Guid.NewGuid(), Guid.NewGuid(), dataDate, bac, pv, ev, ac,
        EacVariant.CpiBased, performanceFactor: null, eac: null, etc: null, vac: null,
        createdAt: dataDate, createdByUserId: null);

    private static ProjectEvmSettings DefaultSettings(DateTimeOffset projectDataDate) => new(
        Bac: 1_000_000.00m, ProjectDataDate: projectDataDate,
        EacVariantDefault: EacVariant.CpiBased, EacCustomPerformanceFactor: null, EacManualEtc: null);

    private static (GetCashFlowQueryHandler Handler, FakeActualCostReader CostReader, FakeEvmSnapshotRepository Snapshots) CreateHandler(
        DateTimeOffset projectDataDate)
    {
        var dataReader = new FakeEvmDataReader { SettingsToReturn = DefaultSettings(projectDataDate) };
        var costReader = new FakeActualCostReader();
        var computationService = new EvmComputationService(dataReader, costReader);
        var snapshots = new FakeEvmSnapshotRepository();
        return (new GetCashFlowQueryHandler(computationService, snapshots), costReader, snapshots);
    }

    [Fact]
    public async Task Handle_Returns_ProjectNotFound_When_The_Project_Does_Not_Exist()
    {
        var dataReader = new FakeEvmDataReader { SettingsToReturn = null };
        var computationService = new EvmComputationService(dataReader, new FakeActualCostReader());
        var handler = new GetCashFlowQueryHandler(computationService, new FakeEvmSnapshotRepository());

        var result = await handler.Handle(new GetCashFlowQuery(Guid.NewGuid(), null, null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(CashFlowErrorCodes.ProjectNotFound, result.Error);
    }

    [Fact]
    public async Task Handle_Rejects_A_From_Date_Later_Than_The_Effective_Data_Date()
    {
        var dataDate = DateTimeOffset.Parse("2026-03-31T00:00:00+07:00");
        var (handler, _, _) = CreateHandler(dataDate);

        var result = await handler.Handle(
            new GetCashFlowQuery(Guid.NewGuid(), dataDate, dataDate.AddDays(1)), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(CashFlowErrorCodes.InvalidRange, result.Error);
    }

    [Fact]
    public async Task Handle_With_No_Closed_Snapshots_Returns_A_Single_Live_Period_Spanning_Since_Inception()
    {
        var dataDate = DateTimeOffset.Parse("2026-03-31T00:00:00+07:00");
        var (handler, costReader, _) = CreateHandler(dataDate);
        costReader.DefaultAmount = 260_000.00m;

        var result = await handler.Handle(new GetCashFlowQuery(Guid.NewGuid(), dataDate, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var period = Assert.Single(result.Value.Periods);
        Assert.Null(period.PeriodStart);
        Assert.Equal(dataDate, period.PeriodEnd);
        Assert.False(period.IsClosed);
        Assert.Equal(260_000.00m, period.AcPeriod);
        Assert.Equal(260_000.00m, period.AcCumulative);
        Assert.Equal(260_000.00m, result.Value.AcCumulative);
    }

    [Fact]
    public async Task Handle_Derives_Closed_Period_Deltas_By_Subtracting_Consecutive_Snapshots_Never_Resumming()
    {
        var jan = DateTimeOffset.Parse("2026-01-31T00:00:00+07:00");
        var feb = DateTimeOffset.Parse("2026-02-28T00:00:00+07:00");
        var dataDate = feb; // effective data date coincides with the last snapshot -> no live tail.
        var dataReader = new FakeEvmDataReader
        {
            SettingsToReturn = DefaultSettings(dataDate),
            // Fully elapsed by `feb` (PlannedFinish before Jan) -> PV = BudgetCost * 100% = 200,000,
            // and EV = BudgetCost * 90% = 180,000 - both chosen to agree exactly with the Feb
            // snapshot below, so the top-level (always-live) headline matches the frozen snapshot
            // and this test exercises the ordinary "no restatement happened" case. See
            // Handle_Warns_When_The_Live_Headline_Disagrees_... for the opposite case.
            ActivityInputsToReturn =
            [
                new EvmActivityProgressInput(
                    Guid.NewGuid(), 200_000.00m,
                    DateTimeOffset.Parse("2025-01-01T00:00:00+07:00"), DateTimeOffset.Parse("2026-01-01T00:00:00+07:00"),
                    90m),
            ],
        };
        var costReader = new FakeActualCostReader();
        var computationService = new EvmComputationService(dataReader, costReader);
        var snapshots = new FakeEvmSnapshotRepository();
        var handler = new GetCashFlowQueryHandler(computationService, snapshots);

        snapshots.Snapshots.Add(Snapshot(jan, 1_000_000m, pv: 100_000m, ev: 90_000m, ac: 80_000m));
        snapshots.Snapshots.Add(Snapshot(feb, 1_000_000m, pv: 200_000m, ev: 180_000m, ac: 170_000m));
        costReader.AmountByDate[feb] = 170_000.00m;

        var result = await handler.Handle(new GetCashFlowQuery(Guid.NewGuid(), dataDate, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Periods.Count);

        var janBucket = result.Value.Periods[0];
        Assert.Null(janBucket.PeriodStart);
        Assert.Equal(jan, janBucket.PeriodEnd);
        Assert.True(janBucket.IsClosed);
        Assert.Equal(80_000.00m, janBucket.AcPeriod); // 80,000 - 0.
        Assert.Equal(80_000.00m, janBucket.AcCumulative);

        var febBucket = result.Value.Periods[1];
        Assert.Equal(jan, febBucket.PeriodStart);
        Assert.Equal(feb, febBucket.PeriodEnd);
        Assert.True(febBucket.IsClosed);
        Assert.Equal(90_000.00m, febBucket.AcPeriod); // 170,000 - 80,000 = a plain subtraction, never a resummed ledger query.
        Assert.Equal(170_000.00m, febBucket.AcCumulative);

        // No live tail: the last snapshot already sits exactly at the effective data date.
        Assert.Equal(170_000.00m, result.Value.AcCumulative);
        Assert.Equal(febBucket.AcCumulative, result.Value.AcCumulative);
        Assert.DoesNotContain(CashFlowWarningCodes.PeriodRestated, result.Value.Warnings);
    }

    [Fact]
    public async Task Handle_Warns_When_The_Live_Headline_Disagrees_With_The_Last_Closed_Snapshot_At_The_Same_Date()
    {
        // ADR-0013 §6.4: a backdated ActualCostEntry correction landed after Feb closed, so the live
        // recomputation at Feb's own data date (330,000) now legitimately differs from what was
        // frozen at close time (170,000) - a restatement, not a bug, but it must be surfaced rather
        // than silently letting the top-level headline and Periods[]'s last closed bucket disagree.
        var jan = DateTimeOffset.Parse("2026-01-31T00:00:00+07:00");
        var feb = DateTimeOffset.Parse("2026-02-28T00:00:00+07:00");
        var (handler, costReader, snapshots) = CreateHandler(feb);
        snapshots.Snapshots.Add(Snapshot(jan, 1_000_000m, pv: 100_000m, ev: 90_000m, ac: 80_000m));
        snapshots.Snapshots.Add(Snapshot(feb, 1_000_000m, pv: 200_000m, ev: 180_000m, ac: 170_000m));
        costReader.AmountByDate[feb] = 330_000.00m; // live AC(Feb) now disagrees with the frozen 170,000.

        var result = await handler.Handle(new GetCashFlowQuery(Guid.NewGuid(), feb, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(CashFlowWarningCodes.PeriodRestated, result.Value.Warnings);
        // The top-level headline still reflects the live truth (consistent with GetEvmQuery)...
        Assert.Equal(330_000.00m, result.Value.AcCumulative);
        // ...while Periods[] keeps showing what was actually frozen at close time - the warning is
        // what lets a consumer explain the two numbers rather than silently disagreeing.
        Assert.Equal(170_000.00m, result.Value.Periods[^1].AcCumulative);
    }

    [Fact]
    public async Task Handle_Appends_A_Live_Trailing_Bucket_After_The_Last_Closed_Snapshot()
    {
        var jan = DateTimeOffset.Parse("2026-01-31T00:00:00+07:00");
        var dataDate = DateTimeOffset.Parse("2026-03-31T00:00:00+07:00");
        var (handler, costReader, snapshots) = CreateHandler(dataDate);
        snapshots.Snapshots.Add(Snapshot(jan, 1_000_000m, pv: 100_000m, ev: 90_000m, ac: 80_000m));
        costReader.AmountByDate[dataDate] = 260_000.00m; // AC(dataDate) via the live EVM engine call.

        var result = await handler.Handle(new GetCashFlowQuery(Guid.NewGuid(), dataDate, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Periods.Count);

        var liveBucket = result.Value.Periods[^1];
        Assert.Equal(jan, liveBucket.PeriodStart);
        Assert.Equal(dataDate, liveBucket.PeriodEnd);
        Assert.False(liveBucket.IsClosed);
        Assert.Equal(180_000.00m, liveBucket.AcPeriod); // 260,000 - 80,000.
        Assert.Equal(260_000.00m, liveBucket.AcCumulative);
        Assert.Equal(260_000.00m, result.Value.AcCumulative);

        // Structural identity: period deltas sum back to the cumulative total - the whole point of
        // never re-summing the ledger a second time.
        Assert.Equal(result.Value.AcCumulative, result.Value.Periods.Sum(p => p.AcPeriod));
    }

    [Fact]
    public async Task Handle_Uses_An_Existing_Snapshot_At_From_As_The_Baseline_Rather_Than_A_Second_Live_Call()
    {
        var jan = DateTimeOffset.Parse("2026-01-31T00:00:00+07:00");
        var feb = DateTimeOffset.Parse("2026-02-28T00:00:00+07:00");
        var (handler, _, snapshots) = CreateHandler(feb);
        snapshots.Snapshots.Add(Snapshot(jan, 1_000_000m, pv: 100_000m, ev: 90_000m, ac: 80_000m));
        snapshots.Snapshots.Add(Snapshot(feb, 1_000_000m, pv: 200_000m, ev: 180_000m, ac: 170_000m));

        var result = await handler.Handle(new GetCashFlowQuery(Guid.NewGuid(), feb, jan), CancellationToken.None);

        Assert.True(result.IsSuccess);
        // The January snapshot is consumed as the baseline, not re-emitted as its own zero-width
        // bucket - only the February delta bucket remains.
        var bucket = Assert.Single(result.Value.Periods);
        Assert.Equal(jan, bucket.PeriodStart);
        Assert.Equal(feb, bucket.PeriodEnd);
        Assert.Equal(90_000.00m, bucket.AcPeriod);
    }

    [Fact]
    public async Task Handle_Always_Reports_Receipts_As_Unavailable_Never_A_Fabricated_Zero()
    {
        var dataDate = DateTimeOffset.Parse("2026-03-31T00:00:00+07:00");
        var (handler, _, _) = CreateHandler(dataDate);

        var result = await handler.Handle(new GetCashFlowQuery(Guid.NewGuid(), dataDate, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.Receipts.IsAvailable);
        Assert.Null(result.Value.Receipts.Cumulative);
        Assert.Empty(result.Value.Receipts.Periods);
        Assert.Equal(CashFlowReceiptsUnavailableReason.PaymentCertificatesNotYetImplemented, result.Value.Receipts.UnavailableReason);
        Assert.Null(result.Value.NetCashPosition);
    }

    [Fact]
    public async Task Handle_Propagates_The_Negative_Actual_Cost_Warning_From_The_Eac_Calculator_Unchanged()
    {
        var dataDate = DateTimeOffset.Parse("2026-03-31T00:00:00+07:00");
        var (handler, costReader, _) = CreateHandler(dataDate);
        costReader.DefaultAmount = -50_000.00m; // over-reversal/credit note (actual-cost.md §7.6).

        var result = await handler.Handle(new GetCashFlowQuery(Guid.NewGuid(), dataDate, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(EvmWarningCodes.ActualCostIsNegative, result.Value.Warnings);
    }
}
