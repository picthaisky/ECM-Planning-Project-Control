namespace CMPlus.Domain.Enums;

/// <summary>
/// The Thai 5-หมวด job-cost structure an <see cref="Entities.ActualCostEntry"/> is categorised
/// into (.claude/knowledge/domain/actual-cost.md §3, ADR-0013) - drives the category-breakdown
/// chart and the §7.4 scope-match check against budgeted cost categories.
/// </summary>
public enum CostCategory
{
    /// <summary>ค่าวัสดุ.</summary>
    Material = 1,

    /// <summary>ค่าแรง.</summary>
    Labour = 2,

    /// <summary>ค่าผู้รับเหมาช่วง.</summary>
    Subcontract = 3,

    /// <summary>ค่าเครื่องจักร/เครื่องมือ.</summary>
    PlantEquipment = 4,

    /// <summary>ค่าดำเนินการหน่วยงาน/โสหุ้ยสนาม.</summary>
    SiteOverhead = 5,

    Other = 6,
}
