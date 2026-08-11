namespace CMPlus.Domain.Enums;

/// <summary>
/// Derived, never independently settable, from the sign of <c>VariationOrder.Amount</c>
/// (domain-rules.md §1: "<c>Type</c> is derived from the sign of <c>Amount</c>, never
/// independently settable, so the two can never disagree"). <see cref="VariationOrder.Type"/> is
/// a computed property with no backing column - see that type's own remarks.
/// </summary>
public enum VariationOrderType
{
    /// <summary>$A \ge 0$ - งานเพิ่ม. Zero is deliberately grouped here (a time-only/equal-value
    /// variation is not an omission of anything - domain-rules.md §7.3 permits $A=0$).</summary>
    Add = 1,

    /// <summary>$A &lt; 0$ - งานลด.</summary>
    Deduct = 2,
}
