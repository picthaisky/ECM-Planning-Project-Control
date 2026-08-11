using CMPlus.Domain.Common;
using CMPlus.Domain.Entities;

namespace CMPlus.Domain.Tests.Entities;

public class BaselineTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid CapturedByUserId = Guid.NewGuid();
    private static readonly DateTimeOffset CapturedAt = DateTimeOffset.Parse("2026-08-11T09:00:00+07:00");

    private static Baseline CreateBaseline(IReadOnlyCollection<BaselineActivitySnapshotInput>? snapshots = null) =>
        Baseline.Capture(TenantId, ProjectId, "Revised Baseline - Rev.2", CapturedAt, CapturedByUserId, 492_400_000.00m,
            snapshots ?? []);

    [Fact]
    public void Capture_Is_Never_Active()
    {
        // docs/10 §9 S14-BE-01: capture and activation are two separate, deliberate actions -
        // mirrors the prototype's "+ บันทึก Baseline ใหม่" vs "ตั้งเป็น Active" buttons.
        var baseline = CreateBaseline();

        Assert.False(baseline.IsActive);
    }

    [Fact]
    public void Capture_Snapshots_Every_Supplied_Activity()
    {
        var activityId1 = Guid.NewGuid();
        var activityId2 = Guid.NewGuid();
        var start = DateTimeOffset.Parse("2026-01-01T00:00:00+07:00");

        var baseline = CreateBaseline(
        [
            new BaselineActivitySnapshotInput(activityId1, start, start.AddDays(14), 14, 1_000_000.00m),
            new BaselineActivitySnapshotInput(activityId2, start.AddDays(14), start.AddDays(24), 10, 500_000.00m),
        ]);

        Assert.Equal(2, baseline.ActivityCount);
        Assert.Equal(2, baseline.Snapshots.Count);
        Assert.Contains(baseline.Snapshots, s => s.ActivityId == activityId1 && s.BudgetCost == 1_000_000.00m);
        Assert.Contains(baseline.Snapshots, s => s.ActivityId == activityId2 && s.DurationDays == 10);
        Assert.All(baseline.Snapshots, s => Assert.Equal(baseline.Id, s.BaselineId));
        Assert.All(baseline.Snapshots, s => Assert.Equal(TenantId, s.TenantId));
    }

    [Fact]
    public void Capture_Allows_An_Empty_Activity_Set()
    {
        // A project with zero activities yet still captures a valid, empty baseline - mirrors
        // CpmRun.Capture's identical allowance.
        var baseline = CreateBaseline([]);

        Assert.Empty(baseline.Snapshots);
        Assert.Equal(0, baseline.ActivityCount);
    }

    [Fact]
    public void Capture_Rejects_An_Empty_ProjectId()
    {
        Assert.Throws<DomainException>(() =>
            Baseline.Capture(TenantId, Guid.Empty, "Name", CapturedAt, CapturedByUserId, 1_000_000m, []));
    }

    [Fact]
    public void Capture_Rejects_An_Empty_CapturedByUserId()
    {
        // Fail-closed on a null/absent actor (this task's standing requirement) - the entity itself
        // refuses a fabricated Guid.Empty actor, defense in depth alongside
        // CaptureBaselineCommandHandler's own pre-check against ICurrentUserContext.UserId.
        Assert.Throws<DomainException>(() =>
            Baseline.Capture(TenantId, ProjectId, "Name", CapturedAt, Guid.Empty, 1_000_000m, []));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Capture_Rejects_A_Missing_Name(string? name)
    {
        Assert.Throws<DomainException>(() =>
            Baseline.Capture(TenantId, ProjectId, name!, CapturedAt, CapturedByUserId, 1_000_000m, []));
    }

    [Fact]
    public void Capture_Rejects_A_Negative_Bac()
    {
        Assert.Throws<DomainException>(() =>
            Baseline.Capture(TenantId, ProjectId, "Name", CapturedAt, CapturedByUserId, -1.00m, []));
    }

    [Fact]
    public void Activate_Sets_IsActive_True()
    {
        var baseline = CreateBaseline();

        baseline.Activate();

        Assert.True(baseline.IsActive);
    }

    [Fact]
    public void Deactivate_Sets_IsActive_False()
    {
        var baseline = CreateBaseline();
        baseline.Activate();

        baseline.Deactivate();

        Assert.False(baseline.IsActive);
    }

    [Fact]
    public void Activate_Is_Idempotent()
    {
        var baseline = CreateBaseline();

        baseline.Activate();
        baseline.Activate();

        Assert.True(baseline.IsActive);
    }
}

public class BaselineActivitySnapshotTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid CapturedByUserId = Guid.NewGuid();

    [Fact]
    public void Capture_Rejects_A_Negative_DurationDays()
    {
        var start = DateTimeOffset.Parse("2026-01-01T00:00:00+07:00");

        Assert.Throws<DomainException>(() => Baseline.Capture(
            TenantId, ProjectId, "Name", start, CapturedByUserId, 1_000_000m,
            [new BaselineActivitySnapshotInput(Guid.NewGuid(), start, start.AddDays(5), -1, 1_000m)]));
    }

    [Fact]
    public void Capture_Rejects_A_Negative_BudgetCost()
    {
        var start = DateTimeOffset.Parse("2026-01-01T00:00:00+07:00");

        Assert.Throws<DomainException>(() => Baseline.Capture(
            TenantId, ProjectId, "Name", start, CapturedByUserId, 1_000_000m,
            [new BaselineActivitySnapshotInput(Guid.NewGuid(), start, start.AddDays(5), 5, -1.00m)]));
    }

    [Fact]
    public void Capture_Rejects_An_Empty_ActivityId()
    {
        var start = DateTimeOffset.Parse("2026-01-01T00:00:00+07:00");

        Assert.Throws<DomainException>(() => Baseline.Capture(
            TenantId, ProjectId, "Name", start, CapturedByUserId, 1_000_000m,
            [new BaselineActivitySnapshotInput(Guid.Empty, start, start.AddDays(5), 5, 1_000m)]));
    }
}
