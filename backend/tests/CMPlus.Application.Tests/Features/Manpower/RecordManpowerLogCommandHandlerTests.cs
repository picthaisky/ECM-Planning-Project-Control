using CMPlus.Application.Features.Manpower;
using CMPlus.Application.Features.Manpower.Commands.RecordManpowerLog;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Tests.Features.Manpower;

/// <summary>S12-BE-02: <see cref="RecordManpowerLogCommandHandler"/> unit tests against
/// <see cref="FakeManpowerEquipmentLogRepository"/> - mirrors
/// <c>RecordWeatherLogCommandHandlerTests</c>' established shape.</summary>
public class RecordManpowerLogCommandHandlerTests
{
    private static RecordManpowerLogCommand BuildCommand(
        Guid projectId, Guid workCategoryId, Guid? wbsNodeId = null, Guid? activityId = null, bool allowDuplicate = false) =>
        new(
            projectId,
            DateTimeOffset.Parse("2026-07-09T00:00:00+07:00"),
            Shift.Day,
            workCategoryId,
            wbsNodeId,
            activityId,
            LabourType.OwnDirect,
            null,
            25,
            200.00m,
            0m,
            false,
            0,
            0m,
            0m,
            "งานโครงสร้าง",
            null,
            allowDuplicate);

    [Fact]
    public async Task Handle_Returns_ProjectNotFound_When_The_Project_Does_Not_Exist()
    {
        var repository = new FakeManpowerEquipmentLogRepository { ProjectExists = false };
        var handler = new RecordManpowerLogCommandHandler(
            repository, new FakeTenantProvider(Guid.NewGuid()), new FakeCurrentUserContext(Guid.NewGuid()), new FakeClock(DateTimeOffset.UtcNow));

        var result = await handler.Handle(BuildCommand(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ManpowerLogErrorCodes.ProjectNotFound, result.Error);
    }

    [Fact]
    public async Task Handle_Fails_Closed_On_A_Null_Actor_Rather_Than_Fabricating_GuidEmpty()
    {
        // L-01 pattern (this task's brief): never `?? Guid.Empty`.
        var repository = new FakeManpowerEquipmentLogRepository();
        var handler = new RecordManpowerLogCommandHandler(
            repository, new FakeTenantProvider(Guid.NewGuid()), new FakeCurrentUserContext(userId: null), new FakeClock(DateTimeOffset.UtcNow));

        var result = await handler.Handle(BuildCommand(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ManpowerLogErrorCodes.ActorRequired, result.Error);
        Assert.Equal(0, repository.AddCallCount);
    }

    [Fact]
    public async Task Handle_Returns_WorkCategoryNotInProject_When_The_Category_Does_Not_Resolve()
    {
        var repository = new FakeManpowerEquipmentLogRepository(); // ExistingWorkCategoryIds left empty
        var handler = new RecordManpowerLogCommandHandler(
            repository, new FakeTenantProvider(Guid.NewGuid()), new FakeCurrentUserContext(Guid.NewGuid()), new FakeClock(DateTimeOffset.UtcNow));

        var result = await handler.Handle(BuildCommand(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ManpowerLogErrorCodes.WorkCategoryNotInProject, result.Error);
    }

    [Fact]
    public async Task Handle_Returns_WbsNodeNotFound_404_For_A_Cross_Tenant_WbsNodeId_Never_422()
    {
        // Fixture M-14b: cross-tenant is indistinguishable from unknown.
        var workCategoryId = Guid.NewGuid();
        var wbsNodeId = Guid.NewGuid();
        var repository = new FakeManpowerEquipmentLogRepository { ExistingWorkCategoryIds = [workCategoryId] };
        // Not in project, not in tenant either -> 404.
        var handler = new RecordManpowerLogCommandHandler(
            repository, new FakeTenantProvider(Guid.NewGuid()), new FakeCurrentUserContext(Guid.NewGuid()), new FakeClock(DateTimeOffset.UtcNow));

        var result = await handler.Handle(BuildCommand(Guid.NewGuid(), workCategoryId, wbsNodeId), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ManpowerLogErrorCodes.WbsNodeNotFound, result.Error);
    }

    [Fact]
    public async Task Handle_Returns_WbsNodeNotInProject_422_When_The_Node_Belongs_To_Another_Project_In_The_Same_Tenant()
    {
        var workCategoryId = Guid.NewGuid();
        var wbsNodeId = Guid.NewGuid();
        var repository = new FakeManpowerEquipmentLogRepository
        {
            ExistingWorkCategoryIds = [workCategoryId],
            WbsNodeIdsInTenant = [wbsNodeId], // resolves in the tenant, but not in THIS project
        };
        var handler = new RecordManpowerLogCommandHandler(
            repository, new FakeTenantProvider(Guid.NewGuid()), new FakeCurrentUserContext(Guid.NewGuid()), new FakeClock(DateTimeOffset.UtcNow));

        var result = await handler.Handle(BuildCommand(Guid.NewGuid(), workCategoryId, wbsNodeId), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ManpowerLogErrorCodes.WbsNodeNotInProject, result.Error);
    }

    [Fact]
    public async Task Handle_Returns_ActivityWbsNodeMismatch_When_The_Activitys_Own_Node_Disagrees_With_The_Rows_Node()
    {
        var workCategoryId = Guid.NewGuid();
        var wbsNodeId = Guid.NewGuid();
        var activityId = Guid.NewGuid();
        var activityOwnWbsNodeId = Guid.NewGuid(); // deliberately different from wbsNodeId
        var repository = new FakeManpowerEquipmentLogRepository
        {
            ExistingWorkCategoryIds = [workCategoryId],
            ExistingWbsNodeIds = [wbsNodeId],
            ExistingActivitiesWithWbsNode = new Dictionary<Guid, Guid> { [activityId] = activityOwnWbsNodeId },
        };
        var handler = new RecordManpowerLogCommandHandler(
            repository, new FakeTenantProvider(Guid.NewGuid()), new FakeCurrentUserContext(Guid.NewGuid()), new FakeClock(DateTimeOffset.UtcNow));

        var result = await handler.Handle(BuildCommand(Guid.NewGuid(), workCategoryId, wbsNodeId, activityId), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ManpowerLogErrorCodes.ActivityWbsNodeMismatch, result.Error);
    }

    [Fact]
    public async Task Handle_Returns_409_AlreadyExists_On_A_Duplicate_Natural_Key_Unless_AllowDuplicate_Is_Set()
    {
        // §4.4/Q8: warn-and-confirm, not a hard block.
        var workCategoryId = Guid.NewGuid();
        var repository = new FakeManpowerEquipmentLogRepository
        {
            ExistingWorkCategoryIds = [workCategoryId],
            HasInForceOriginalForNaturalKey = true,
        };
        var handler = new RecordManpowerLogCommandHandler(
            repository, new FakeTenantProvider(Guid.NewGuid()), new FakeCurrentUserContext(Guid.NewGuid()), new FakeClock(DateTimeOffset.UtcNow));

        var blocked = await handler.Handle(BuildCommand(Guid.NewGuid(), workCategoryId, allowDuplicate: false), CancellationToken.None);
        Assert.True(blocked.IsFailure);
        Assert.Equal(ManpowerLogErrorCodes.AlreadyExists, blocked.Error);

        var overridden = await handler.Handle(BuildCommand(Guid.NewGuid(), workCategoryId, allowDuplicate: true), CancellationToken.None);
        Assert.True(overridden.IsSuccess);
        Assert.True(overridden.Value.AllowDuplicateOverride);
    }

    [Fact]
    public async Task Handle_Succeeds_And_Persists_An_Original_Entry_For_A_Well_Formed_Request()
    {
        var tenantId = Guid.NewGuid();
        var recordedByUserId = Guid.NewGuid();
        var now = DateTimeOffset.Parse("2026-07-09T18:00:00+07:00");
        var workCategoryId = Guid.NewGuid();
        var repository = new FakeManpowerEquipmentLogRepository { ExistingWorkCategoryIds = [workCategoryId] };
        var handler = new RecordManpowerLogCommandHandler(
            repository, new FakeTenantProvider(tenantId), new FakeCurrentUserContext(recordedByUserId), new FakeClock(now));

        var result = await handler.Handle(BuildCommand(Guid.NewGuid(), workCategoryId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, repository.AddCallCount);
        Assert.Equal(ManpowerLogEntryKind.Original, result.Value.EntryKind);
        Assert.Equal(tenantId, repository.AddedLog!.TenantId);
        Assert.Equal(recordedByUserId, result.Value.RecordedByUserId);
        Assert.Equal(now, result.Value.RecordedAt);
        Assert.Equal(200.00m, result.Value.ManHours);
    }
}
