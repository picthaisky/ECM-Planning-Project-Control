namespace CMPlus.Application.Features.Eot;

/// <summary>Stable <see cref="CMPlus.Domain.Common.Result"/> error codes for S11-BE-02's
/// <c>EvaluateEotCommand</c> (domain-rules.md weather-eot).</summary>
public static class EotErrorCodes
{
    public const string ProjectNotFound = "EotProjectNotFound";

    /// <summary>Sprint 10's L-01 fix pattern: no resolvable authenticated actor. Structurally
    /// unreachable behind <c>[Authorize]</c> in production; kept as a fail-closed guard. Maps to 401.</summary>
    public const string ActorRequired = "EotActorRequired";

    /// <summary>domain-rules.md §3.3: the project has no <c>Calendar.IsDefault</c> row. "No default
    /// calendar ⟹ block, never guess" - same fail-closed discipline as <c>ApprovalPolicyGap</c>. Maps to 422.</summary>
    public const string ProjectCalendarNotConfigured = "EotProjectCalendarNotConfigured";

    /// <summary>domain-rules.md §4.4: the project has never had a single <c>CpmRun</c> captured, so
    /// the contemporaneous criticality ruling (§4.1) cannot be answered even in degraded form. Maps to 422.</summary>
    public const string NoCpmRunAvailable = "EotNoCpmRunAvailable";

    /// <summary>domain-rules.md §5.1: a governing <c>CpmRun</c>'s stored <c>ProjectDurationDays</c>
    /// disagrees with re-running <c>CpmEngine</c> against its own captured network - "a mismatch
    /// means the stored run is corrupt and the evaluation must fail rather than proceed". Maps to 500
    /// (an infrastructure-integrity problem, never a user input error).</summary>
    public const string CpmRunDataCorrupt = "EotCpmRunDataCorrupt";

    /// <summary>The requested evaluation window's end precedes its start. Maps to 400.</summary>
    public const string InvalidWindowRange = "EotInvalidWindowRange";
}
