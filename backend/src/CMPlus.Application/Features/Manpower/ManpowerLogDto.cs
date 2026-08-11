using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Features.Manpower;

/// <summary>Wire shape for a <see cref="ManpowerEquipmentLog"/> (domain-rules.md (manpower-equipment)
/// §4.1) - shared by <c>RecordManpowerLogCommandHandler</c>/<c>RecordManpowerLogCorrectionCommandHandler</c>
/// (mirrors <c>WeatherLogDto.From(entity)</c>'s pattern).</summary>
public sealed record ManpowerLogDto(
    Guid Id,
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
    Guid RecordedByUserId,
    DateTimeOffset RecordedAt,
    ManpowerLogEntryKind EntryKind,
    Guid? CorrectsLogId,
    string? CorrectionReason,
    bool AllowDuplicateOverride)
{
    public static ManpowerLogDto From(ManpowerEquipmentLog log) => new(
        log.Id,
        log.ProjectId,
        log.LogDate,
        log.Shift,
        log.WorkCategoryId,
        log.WbsNodeId,
        log.ActivityId,
        log.LabourType,
        log.SubcontractorRef,
        log.WorkerCount,
        log.ManHours,
        log.OvertimeHours,
        log.ManHoursDerived,
        log.EquipmentCount,
        log.EquipmentOperatingHours,
        log.EquipmentStandbyHours,
        log.WorkDescription,
        log.RelatedWeatherLogId,
        log.RecordedByUserId,
        log.RecordedAt,
        log.EntryKind,
        log.CorrectsLogId,
        log.CorrectionReason,
        log.AllowDuplicateOverride);
}
