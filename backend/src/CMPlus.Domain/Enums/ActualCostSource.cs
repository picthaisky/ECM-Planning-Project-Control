namespace CMPlus.Domain.Enums;

/// <summary>
/// Where one <see cref="Entities.ActualCostEntry"/> row came from
/// (.claude/knowledge/domain/actual-cost.md §4/§9, ADR-0013) - provenance for reconciliation and
/// for the "reverse wholesale" workflow if an opt-in P6/ERP import is later found to be wrong.
/// </summary>
public enum ActualCostSource
{
    /// <summary>QS/PM manual entry - the only source this sprint's write path
    /// (<c>RecordActualCostCommand</c>) ever produces; accruals in particular can only ever come
    /// from here (actual-cost.md §4: "a project-control judgement, not an accounting record").</summary>
    ManualEntry = 1,

    /// <summary>The monthly job-cost ledger, imported by cost code (Sprint 9 - not built yet).</summary>
    ExcelImport = 2,

    /// <summary>An opt-in P6/MSP/ERP actual-cost import (actual-cost.md §11) - never a default,
    /// since most Thai P6 users never maintain resource actuals.</summary>
    ErpIntegration = 3,

    /// <summary>Estimated from <c>ManpowerEquipmentLog</c> + resource rates (Sprint 12 at the
    /// earliest, actual-cost.md §4.3) - must be visually distinguished as an estimate and
    /// superseded, never summed alongside a real accounting figure once one lands.</summary>
    DerivedFromResourceLog = 4,
}
