using CMPlus.Domain.Enums;

namespace CMPlus.WebApi.Controllers.Manpower;

/// <summary>Request body for <c>POST /api/v1/projects/{projectId}/manpower-logs</c> (Original
/// entries only - domain-rules.md (manpower-equipment) §4.7).</summary>
public sealed record RecordManpowerLogRequest(
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
    bool AllowDuplicate = false);

/// <summary>Request body for <c>POST /api/v1/projects/{projectId}/manpower-logs/{logId}/corrections</c>.
/// <c>CorrectsLogId</c> is deliberately NOT a field here - it always comes from the route
/// (domain-rules.md §4.7).</summary>
public sealed record RecordManpowerLogCorrectionRequest(
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
    Guid? RelatedWeatherLogId);
