namespace CMPlus.Domain.Common;

/// <summary>
/// Shared invariant helper for <c>decimal(18,2)</c> money properties that must never be negative
/// (e.g. <c>Project.BAC</c>, <c>Project.ContractValue</c>). Throws <see cref="DomainException"/>
/// rather than clamping, because a negative money value is always a caller bug, not a legitimate
/// input to silently coerce.
/// </summary>
public static class MoneyGuard
{
    public static decimal EnsureNonNegative(decimal value, string propertyName) =>
        value >= 0
            ? value
            : throw new DomainException($"{propertyName} cannot be negative.");

    public static decimal? EnsureNonNegative(decimal? value, string propertyName) =>
        value is null || value >= 0
            ? value
            : throw new DomainException($"{propertyName} cannot be negative.");
}
