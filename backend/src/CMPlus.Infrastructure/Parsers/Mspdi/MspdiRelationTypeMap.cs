using CMPlus.Domain.Enums;

namespace CMPlus.Infrastructure.Parsers.Mspdi;

/// <summary>Maps MSPDI's <c>PredecessorLink/Type</c> integer codes (the MSPDI schema's fixed
/// vocabulary: 0=FF, 1=FS, 2=SF, 3=SS) to <see cref="RelationType"/>.</summary>
internal static class MspdiRelationTypeMap
{
    public static bool TryMap(string typeText, out RelationType relationType)
    {
        switch (typeText.Trim())
        {
            case "0":
                relationType = RelationType.FF;
                return true;
            case "1":
                relationType = RelationType.FS;
                return true;
            case "2":
                relationType = RelationType.SF;
                return true;
            case "3":
                relationType = RelationType.SS;
                return true;
            default:
                relationType = default;
                return false;
        }
    }
}
