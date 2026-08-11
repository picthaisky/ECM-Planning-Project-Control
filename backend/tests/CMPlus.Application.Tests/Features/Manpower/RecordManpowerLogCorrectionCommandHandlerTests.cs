using CMPlus.Application.Features.Manpower;
using CMPlus.Application.Features.Manpower.Commands.RecordManpowerLogCorrection;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Tests.Features.Manpower;

/// <summary>S12-BE-02, fixture M-13: <see cref="RecordManpowerLogCorrectionCommandHandler"/> unit
/// tests against <see cref="FakeManpowerEquipmentLogRepository"/> - mirrors
/// <c>RecordWeatherLogCorrectionCommandHandlerTests</c>' established shape, exercising §4.7's
/// chain-integrity rules 1/2/4.</summary>
public class RecordManpowerLogCorrectionCommandHandlerTests
{
    private static RecordManpowerLogCorrectionCommand BuildCommand(
        Guid projectId, Guid correctsLogId, Guid workCategoryId, ManpowerLogEntryKind entryKind = ManpowerLogEntryKind.Correction) =>
        new(
            projectId,
            correctsLogId,
            entryKind,
            "ลืมนับ OT 60 ชม.",
            DateTimeOffset.Parse("2026-07-10T00:00:00+07:00"),
            Shift.Day,
            workCategoryId,
            null,
            null,
            LabourType.OwnDirect,
            null,
            75,
            660.00m,
            60.00m,
            false,
            0,
            0m,
            0m,
            null,
            null);

    private static ManpowerEquipmentLog BuildOriginal(Guid projectId, Guid workCategoryId, DateTimeOffset recordedAt) =>
        ManpowerEquipmentLog.CreateOriginal(
            Guid.NewGuid(), projectId, DateTimeOffset.Parse("2026-07-10T00:00:00+07:00"), Shift.Day, workCategoryId,
            null, null, LabourType.OwnDirect, null, 75, 600.00m, 0m, false, 0, 0m, 0m, null, null,
            Guid.NewGuid(), recordedAt, allowDuplicateOverride: false);

    [Fact]
    public async Task Handle_Returns_CorrectionTargetNotFound_When_The_Target_Does_Not_Resolve_In_This_Project()
    {
        var repository = new FakeManpowerEquipmentLogRepository();
        var handler = new RecordManpowerLogCorrectionCommandHandler(
            repository, new FakeTenantProvider(Guid.NewGuid()), new FakeCurrentUserContext(Guid.NewGuid()), new FakeClock(DateTimeOffset.UtcNow));

        var result = await handler.Handle(
            BuildCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ManpowerLogErrorCodes.CorrectionTargetNotFound, result.Error);
    }

    [Fact]
    public async Task Handle_Returns_409_AlreadySuperseded_When_The_Target_Already_Has_A_Correction()
    {
        // §4.7 chain-integrity rule 2 - the load-bearing one.
        var projectId = Guid.NewGuid();
        var workCategoryId = Guid.NewGuid();
        var original = BuildOriginal(projectId, workCategoryId, DateTimeOffset.Parse("2026-07-10T18:00:00+07:00"));
        var existingCorrection = ManpowerEquipmentLog.CreateCorrection(
            Guid.NewGuid(), projectId, original.Id, "first correction",
            original.LogDate, Shift.Day, workCategoryId, null, null, LabourType.OwnDirect, null, 75, 610.00m, 0m,
            false, 0, 0m, 0m, null, null, Guid.NewGuid(), DateTimeOffset.Parse("2026-07-11T09:00:00+07:00"));

        var repository = new FakeManpowerEquipmentLogRepository { ExistingWorkCategoryIds = [workCategoryId] };
        repository.LogsById[original.Id] = original;
        repository.LogsById[existingCorrection.Id] = existingCorrection;

        var handler = new RecordManpowerLogCorrectionCommandHandler(
            repository, new FakeTenantProvider(Guid.NewGuid()), new FakeCurrentUserContext(Guid.NewGuid()),
            new FakeClock(DateTimeOffset.Parse("2026-07-12T09:00:00+07:00")));

        var result = await handler.Handle(BuildCommand(projectId, original.Id, workCategoryId), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ManpowerLogErrorCodes.AlreadySuperseded, result.Error);
    }

    [Fact]
    public async Task Handle_Returns_CorrectionOrdering_When_The_Target_Is_Not_Strictly_Older()
    {
        // §4.7 chain-integrity rule 4.
        var projectId = Guid.NewGuid();
        var workCategoryId = Guid.NewGuid();
        var now = DateTimeOffset.Parse("2026-07-10T18:00:00+07:00");
        var original = BuildOriginal(projectId, workCategoryId, recordedAt: now);

        var repository = new FakeManpowerEquipmentLogRepository { ExistingWorkCategoryIds = [workCategoryId] };
        repository.LogsById[original.Id] = original;

        // Clock set to the SAME instant as the target's own RecordedAt - not strictly older.
        var handler = new RecordManpowerLogCorrectionCommandHandler(
            repository, new FakeTenantProvider(Guid.NewGuid()), new FakeCurrentUserContext(Guid.NewGuid()), new FakeClock(now));

        var result = await handler.Handle(BuildCommand(projectId, original.Id, workCategoryId), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ManpowerLogErrorCodes.CorrectionOrdering, result.Error);
    }

    [Fact]
    public async Task Handle_Succeeds_And_The_Corrections_Own_Values_Govern_Completely()
    {
        // M-13 step 3/4: 75 คน, 660.00h (60h OT omitted from the original) - the correction replaces,
        // it does not patch.
        var projectId = Guid.NewGuid();
        var workCategoryId = Guid.NewGuid();
        var original = BuildOriginal(projectId, workCategoryId, DateTimeOffset.Parse("2026-07-10T18:00:00+07:00"));

        var repository = new FakeManpowerEquipmentLogRepository { ExistingWorkCategoryIds = [workCategoryId] };
        repository.LogsById[original.Id] = original;

        var handler = new RecordManpowerLogCorrectionCommandHandler(
            repository, new FakeTenantProvider(Guid.NewGuid()), new FakeCurrentUserContext(Guid.NewGuid()),
            new FakeClock(DateTimeOffset.Parse("2026-07-11T09:00:00+07:00")));

        var result = await handler.Handle(BuildCommand(projectId, original.Id, workCategoryId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ManpowerLogEntryKind.Correction, result.Value.EntryKind);
        Assert.Equal(original.Id, result.Value.CorrectsLogId);
        Assert.Equal(660.00m, result.Value.ManHours);
        Assert.Equal(60.00m, result.Value.OvertimeHours);
        // The original itself is untouched (append-only - a forward pointer only, §4.7).
        Assert.Equal(600.00m, original.ManHours);
        Assert.Null(original.CorrectsLogId);
    }

    [Fact]
    public async Task Handle_Fails_Closed_On_A_Null_Actor()
    {
        var projectId = Guid.NewGuid();
        var workCategoryId = Guid.NewGuid();
        var original = BuildOriginal(projectId, workCategoryId, DateTimeOffset.Parse("2026-07-10T18:00:00+07:00"));
        var repository = new FakeManpowerEquipmentLogRepository { ExistingWorkCategoryIds = [workCategoryId] };
        repository.LogsById[original.Id] = original;

        var handler = new RecordManpowerLogCorrectionCommandHandler(
            repository, new FakeTenantProvider(Guid.NewGuid()), new FakeCurrentUserContext(userId: null),
            new FakeClock(DateTimeOffset.Parse("2026-07-11T09:00:00+07:00")));

        var result = await handler.Handle(BuildCommand(projectId, original.Id, workCategoryId), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ManpowerLogErrorCodes.ActorRequired, result.Error);
    }
}
