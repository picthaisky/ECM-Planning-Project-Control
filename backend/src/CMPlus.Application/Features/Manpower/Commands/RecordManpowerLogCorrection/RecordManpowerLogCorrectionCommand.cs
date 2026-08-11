using CMPlus.Domain.Common;
using CMPlus.Domain.Enums;
using MediatR;

namespace CMPlus.Application.Features.Manpower.Commands.RecordManpowerLogCorrection;

/// <summary>
/// <c>POST /api/v1/projects/{projectId}/manpower-logs/{logId}/corrections</c> (S12-BE-02,
/// domain-rules.md (manpower-equipment) §4.7) - creates a <see cref="ManpowerLogEntryKind.Correction"/>
/// or <see cref="ManpowerLogEntryKind.Retraction"/> entry. <see cref="CorrectsLogId"/> is always taken
/// from the route (never the request body - the same discipline <c>RecordWeatherLogCorrectionCommand</c>
/// established), so a client cannot aim a correction at an arbitrary row by editing the payload while
/// the URL says otherwise.
/// </summary>
public sealed record RecordManpowerLogCorrectionCommand(
    Guid ProjectId,
    Guid CorrectsLogId,
    ManpowerLogEntryKind EntryKind,
    string CorrectionReason,
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
    Guid? RelatedWeatherLogId) : IRequest<Result<ManpowerLogDto>>;
