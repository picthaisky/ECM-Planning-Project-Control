using CMPlus.Domain.Common;
using CMPlus.Domain.Enums;
using MediatR;

namespace CMPlus.Application.Features.Manpower.Commands.RecordManpowerLog;

/// <summary>
/// <c>POST /api/v1/projects/{projectId}/manpower-logs</c> (S12-BE-02, domain-rules.md
/// (manpower-equipment) §4.7) - creates a <see cref="ManpowerLogEntryKind.Original"/> entry only. A
/// correction/retraction is a separate command (<c>RecordManpowerLogCorrectionCommand</c>) hitting a
/// separate route, per the same two-endpoint split <c>RecordWeatherLogCommand</c> established.
/// <c>RecordedByUserId</c>/<c>RecordedAt</c> are resolved server-side by the handler
/// (<c>ICurrentUserContext</c>/<c>IDateTimeProvider</c>), never trusted from the request.
/// </summary>
public sealed record RecordManpowerLogCommand(
    Guid ProjectId,
    DateTimeOffset LogDate,
    Shift Shift,
    Guid WorkCategoryId,
    Guid? WbsNodeId,
    Guid? ActivityId,
    LabourType LabourType,
    string? SubcontractorRef,
    int WorkerCount,
    decimal ManHours,
    decimal OvertimeHours,
    bool ManHoursDerived,
    int EquipmentCount,
    decimal EquipmentOperatingHours,
    decimal EquipmentStandbyHours,
    string? WorkDescription,
    Guid? RelatedWeatherLogId,
    bool AllowDuplicate) : IRequest<Result<ManpowerLogDto>>;
