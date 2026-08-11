namespace CMPlus.Domain.Enums;

/// <summary>domain-rules.md (manpower-equipment) §4.1/§12 Q7: who the workers on a
/// <see cref="Entities.ManpowerEquipmentLog"/> row work for. Q7's ruling keeps every labour type in
/// the Productivity Index (never <see cref="OwnDirect"/>-only) - unbudgeted subcontract hours are
/// recorded in <c>ExcludedManHours</c> and disclosed via coverage instead of being silently
/// dropped.</summary>
public enum LabourType
{
    OwnDirect = 1,
    Subcontract = 2,
    Hired = 3,
}
