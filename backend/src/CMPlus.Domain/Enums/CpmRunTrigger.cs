namespace CMPlus.Domain.Enums;

/// <summary>What caused a <see cref="CMPlus.Domain.Entities.CpmRun"/> to be captured (ADR-0019,
/// domain-rules.md weather-eot §4.3). Only <see cref="Manual"/> is wired to a real caller today -
/// <c>RecalculateCpmCommandHandler</c>, via the WBS/Gantt screen's "คำนวณ CPM ใหม่" action, itself
/// gated to PM/Planning/Admin (<c>CpmController</c>). The other three members exist so the column
/// never has to be widened later, but nothing in this codebase yet constructs a run with them:
/// Sprint 10's VO-approval CPM re-trigger was explicitly deferred
/// (<c>ApproveVariationOrderCommandHandler</c>'s own remarks: "CPM re-trigger - deliberately not
/// wired"), and neither an Import-triggered nor a System/background recalculation exists at all
/// yet.</summary>
public enum CpmRunTrigger
{
    Manual = 1,
    Import = 2,
    VoApproval = 3,
    System = 4,
}
