namespace CMPlus.Domain.Enums;

/// <summary>domain-rules.md (manpower-equipment) §4.1: a <see cref="Entities.ManpowerEquipmentLog"/>
/// is scoped to one shift, so a site running Day and Night crews on the same
/// (LogDate, WorkCategoryId, WbsNodeId) records two rows, never one merged row - the same
/// deliberate-granularity discipline §4.4 applies to two subcontractors of the same trade.</summary>
public enum Shift
{
    Day = 1,
    Night = 2,
}
