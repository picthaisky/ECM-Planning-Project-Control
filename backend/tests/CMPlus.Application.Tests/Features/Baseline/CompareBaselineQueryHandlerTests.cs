using CMPlus.Application.Abstractions;
using CMPlus.Application.Features.Baseline;
using CMPlus.Application.Features.Baseline.Queries.CompareBaseline;

namespace CMPlus.Application.Tests.Features.Baseline;

public class CompareBaselineQueryHandlerTests
{
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid BaselineId = Guid.NewGuid();
    private static readonly DateTimeOffset CapturedAt = DateTimeOffset.Parse("2026-07-08T00:00:00+07:00");

    private static FakeBaselineComparisonReader CreateReaderWithActiveBaseline(decimal currentBac, decimal baselineBac)
    {
        var reader = new FakeBaselineComparisonReader
        {
            CurrentBac = currentBac,
            ActiveBaseline = new BaselineSummary(BaselineId, "Revised Baseline - Rev.2 (+VO)", CapturedAt, baselineBac),
        };
        reader.BaselinesById[BaselineId] = reader.ActiveBaseline;
        return reader;
    }

    [Fact]
    public async Task Handle_Returns_ProjectNotFound_When_The_Project_Does_Not_Exist()
    {
        var reader = new FakeBaselineComparisonReader { CurrentBac = null };
        var handler = new CompareBaselineQueryHandler(reader);

        var result = await handler.Handle(new CompareBaselineQuery(ProjectId, null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(BaselineErrorCodes.ProjectNotFound, result.Error);
    }

    [Fact]
    public async Task Handle_Returns_NoActiveBaseline_When_None_Is_Active_And_No_BaselineId_Was_Supplied()
    {
        var reader = new FakeBaselineComparisonReader { CurrentBac = 500_000_000.00m, ActiveBaseline = null };
        var handler = new CompareBaselineQueryHandler(reader);

        var result = await handler.Handle(new CompareBaselineQuery(ProjectId, null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(BaselineErrorCodes.NoActiveBaseline, result.Error);
    }

    [Fact]
    public async Task Handle_Returns_NotFound_When_An_Explicit_BaselineId_Does_Not_Resolve()
    {
        var reader = new FakeBaselineComparisonReader { CurrentBac = 500_000_000.00m };
        var handler = new CompareBaselineQueryHandler(reader);

        var result = await handler.Handle(new CompareBaselineQuery(ProjectId, Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(BaselineErrorCodes.NotFound, result.Error);
    }

    [Fact]
    public async Task Handle_Compares_Against_The_Active_Baseline_When_No_BaselineId_Is_Supplied()
    {
        var reader = CreateReaderWithActiveBaseline(492_400_000.00m, 485_000_000.00m);
        var handler = new CompareBaselineQueryHandler(reader);

        var result = await handler.Handle(new CompareBaselineQuery(ProjectId, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(BaselineId, result.Value.BaselineId);
    }

    [Fact]
    public async Task Handle_Compares_Against_An_Explicit_Baseline_When_Supplied_Even_If_Not_Active()
    {
        var reader = new FakeBaselineComparisonReader { CurrentBac = 492_400_000.00m };
        var inactiveBaselineId = Guid.NewGuid();
        reader.BaselinesById[inactiveBaselineId] = new BaselineSummary(inactiveBaselineId, "BL-0 (Original)", CapturedAt, 485_000_000.00m);
        var handler = new CompareBaselineQueryHandler(reader);

        var result = await handler.Handle(new CompareBaselineQuery(ProjectId, inactiveBaselineId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(inactiveBaselineId, result.Value.BaselineId);
    }

    /// <summary>
    /// S14-BE-02's DoD: "delta วัน (ปัจจุบัน − baseline) ต่อกิจกรรม ตรงตัวอย่างใน prototype". Fixture
    /// taken verbatim from the prototype's own <c>ganttData</c> row for ACT-214 ("โครงสร้าง คสล.
    /// ชั้น 6-9"): <c>{ start: 30, dur: 26, blStart: 29, blDur: 24 }</c>, annotated in the prototype
    /// itself as <c>"ACT-214 · TF 0 · ช้า 3 วัน"</c> ("late 3 days") - i.e. the prototype's own
    /// worked finish-variance answer is 3 days, reproduced below by treating each day-offset as
    /// days from an arbitrary project epoch. (The prototype's per-activity <c>meta</c> strings are
    /// free-form annotations on mock data, not all internally consistent with their own start/dur
    /// numbers - ACT-214 is the one row that does reconcile exactly, so it is the fixture used
    /// here; see the backend-developer report.)
    /// </summary>
    [Fact]
    public async Task Handle_Computes_Per_Activity_Deltas_Matching_The_Prototype_Act214_Example()
    {
        var epoch = DateTimeOffset.Parse("2026-01-01T00:00:00+07:00");
        var activityId = Guid.NewGuid();
        var reader = CreateReaderWithActiveBaseline(currentBac: 1_000_000.00m, baselineBac: 1_000_000.00m);
        reader.Rows =
        [
            new BaselineActivityComparisonRow(
                activityId,
                BaselinePlannedStart: epoch.AddDays(29),
                BaselinePlannedFinish: epoch.AddDays(29 + 24),
                BaselineDurationDays: 24,
                BaselineBudgetCost: 1_000_000.00m,
                CurrentActivityCode: "ACT-214",
                CurrentName: "โครงสร้าง คสล. ชั้น 6-9",
                CurrentPlannedStart: epoch.AddDays(30),
                CurrentPlannedFinish: epoch.AddDays(30 + 26),
                CurrentDurationDays: 26,
                CurrentBudgetCost: 1_050_000.00m,
                CurrentIsCritical: true),
        ];
        var handler = new CompareBaselineQueryHandler(reader);

        var result = await handler.Handle(new CompareBaselineQuery(ProjectId, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var row = Assert.Single(result.Value.Activities);
        Assert.False(row.IsRemoved);
        Assert.Equal("ACT-214", row.ActivityCode);
        Assert.Equal(1, row.StartVarianceDays); // 30 - 29
        Assert.Equal(3, row.FinishVarianceDays); // 56 - 53, matches the prototype's own "ช้า 3 วัน"
        Assert.Equal(2, row.DurationVarianceDays); // 26 - 24
        Assert.Equal(50_000.00m, row.BudgetVarianceAmount); // 1,050,000.00 - 1,000,000.00
        Assert.True(row.IsCritical);
        Assert.Equal(1, result.Value.DriftedActivityCount); // FinishVarianceDays > 0
        Assert.Equal(1, result.Value.TotalActivityCount);
        Assert.Equal(3, result.Value.ProjectFinishVarianceDays); // single-activity project: same as the row's own delta
    }

    [Fact]
    public async Task Handle_Marks_A_Snapshot_As_Removed_When_No_Current_Activity_Matches_Any_More()
    {
        var epoch = DateTimeOffset.Parse("2026-01-01T00:00:00+07:00");
        var reader = CreateReaderWithActiveBaseline(1_000_000.00m, 1_000_000.00m);
        reader.Rows =
        [
            new BaselineActivityComparisonRow(
                Guid.NewGuid(), epoch, epoch.AddDays(10), 10, 250_000.00m,
                CurrentActivityCode: null, CurrentName: null,
                CurrentPlannedStart: null, CurrentPlannedFinish: null, CurrentDurationDays: null,
                CurrentBudgetCost: null, CurrentIsCritical: null),
        ];
        var handler = new CompareBaselineQueryHandler(reader);

        var result = await handler.Handle(new CompareBaselineQuery(ProjectId, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var row = Assert.Single(result.Value.Activities);
        Assert.True(row.IsRemoved);
        Assert.Null(row.StartVarianceDays);
        Assert.Null(row.FinishVarianceDays);
        Assert.Null(row.DurationVarianceDays);
        Assert.Null(row.BudgetVarianceAmount);
        // Baseline-side fields survive regardless - the frozen evidence is never lost.
        Assert.Equal(250_000.00m, row.BaselineBudgetCost);
        // A removed activity contributes to neither the drift count nor the project-finish variance
        // (nothing left to be "late").
        Assert.Equal(0, result.Value.DriftedActivityCount);
        Assert.Null(result.Value.ProjectFinishVarianceDays);
    }

    [Fact]
    public async Task Handle_Computes_The_Bac_Variance_Tile_From_Current_Minus_Baseline()
    {
        var reader = CreateReaderWithActiveBaseline(currentBac: 492_400_000.00m, baselineBac: 485_000_000.00m);
        reader.Rows = [];
        var handler = new CompareBaselineQueryHandler(reader);

        var result = await handler.Handle(new CompareBaselineQuery(ProjectId, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(492_400_000.00m, result.Value.CurrentBac);
        Assert.Equal(485_000_000.00m, result.Value.BaselineBac);
        Assert.Equal(7_400_000.00m, result.Value.BacVarianceAmount);
    }

    [Fact]
    public async Task Handle_Counts_Only_Activities_Running_Later_Than_Baseline_As_Drifted()
    {
        var epoch = DateTimeOffset.Parse("2026-01-01T00:00:00+07:00");
        var reader = CreateReaderWithActiveBaseline(1_000_000.00m, 1_000_000.00m);
        reader.Rows =
        [
            // On time (no drift).
            new BaselineActivityComparisonRow(
                Guid.NewGuid(), epoch, epoch.AddDays(10), 10, 100_000.00m,
                "A-1", "On time", epoch, epoch.AddDays(10), 10, 100_000.00m, false),
            // Ahead of schedule (finish variance negative - not drift).
            new BaselineActivityComparisonRow(
                Guid.NewGuid(), epoch, epoch.AddDays(10), 10, 100_000.00m,
                "A-2", "Ahead", epoch, epoch.AddDays(8), 8, 100_000.00m, false),
            // Behind schedule (finish variance positive - drift).
            new BaselineActivityComparisonRow(
                Guid.NewGuid(), epoch, epoch.AddDays(10), 10, 100_000.00m,
                "A-3", "Behind", epoch, epoch.AddDays(13), 13, 100_000.00m, true),
        ];
        var handler = new CompareBaselineQueryHandler(reader);

        var result = await handler.Handle(new CompareBaselineQuery(ProjectId, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.TotalActivityCount);
        Assert.Equal(1, result.Value.DriftedActivityCount);
    }
}
