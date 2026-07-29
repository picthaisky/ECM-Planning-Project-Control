namespace CMPlus.Domain.Common;

/// <summary>
/// Shared invariant helper: every percent-typed property in the domain (<c>decimal(5,2)</c>, per
/// .claude/knowledge/patterns/conventions.md) is clamped to [0,100] at the point of assignment -
/// not merely validated externally (docs/10 S1-BE-01 DoD; domain-decisions.md §1.3, "clamp
/// ProgressPercentage to [0,100] at input"). The nullable overload passes <c>null</c> straight
/// through, so a deliberately-unset percentage (e.g. <c>Project.RetentionRate == null</c>) stays
/// distinguishable from an explicit 0.
/// </summary>
public static class PercentageGuard
{
    public static decimal Clamp(decimal value) => Math.Clamp(value, 0m, 100m);

    public static decimal? Clamp(decimal? value) => value is null ? null : Clamp(value.Value);
}
