using CMPlus.Domain.Enums;

namespace CMPlus.Infrastructure.Parsers.Xer;

/// <summary>Maps XER's <c>TASKPRED.pred_type</c> codes (P6's real, fixed vocabulary) to
/// <see cref="RelationType"/>.</summary>
internal static class XerRelationTypeMap
{
    public static bool TryMap(string predType, out RelationType relationType)
    {
        switch (predType.Trim().ToUpperInvariant())
        {
            case "PR_FS":
                relationType = RelationType.FS;
                return true;
            case "PR_SS":
                relationType = RelationType.SS;
                return true;
            case "PR_FF":
                relationType = RelationType.FF;
                return true;
            case "PR_SF":
                relationType = RelationType.SF;
                return true;
            default:
                relationType = default;
                return false;
        }
    }
}
