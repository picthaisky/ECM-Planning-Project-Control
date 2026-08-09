using CMPlus.Domain.Enums;

namespace CMPlus.Application.Features.Evm;

/// <summary>
/// Strict, case-insensitive `?eacVariant=` parsing for <c>GetEvmQueryHandler</c> - deliberately
/// <b>not</b> a plain <see cref="Enum.TryParse{TEnum}(string, bool, out TEnum)"/> call, which would
/// also silently accept a numeric string (e.g. `"3"` -&gt; <see cref="EacVariant.CpiSpiBased"/>) that
/// no part of the documented API contract (design.md §2.1) ever shows or expects - the only valid
/// input shape is one of the five enum names.
/// </summary>
public static class EacVariantParser
{
    public static bool TryParse(string value, out EacVariant variant)
    {
        foreach (var candidate in Enum.GetValues<EacVariant>())
        {
            if (string.Equals(candidate.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                variant = candidate;
                return true;
            }
        }

        variant = default;
        return false;
    }
}
