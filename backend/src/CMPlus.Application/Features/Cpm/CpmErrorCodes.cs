namespace CMPlus.Application.Features.Cpm;

/// <summary>Stable <see cref="CMPlus.Domain.Common.Result"/> error codes for the CPM feature
/// (S5-BE-04). Mirrors the <c>ProgressErrorCodes</c>/<c>WbsErrorCodes</c> pattern. The graph-level
/// failures (cycle/duplicate/unknown-activity) live in
/// <c>CMPlus.Application.Services.Cpm.CpmValidationErrorCodes</c> instead - those come from the
/// engine itself, not from this feature's own request handling.</summary>
public static class CpmErrorCodes
{
    /// <summary>The requested project does not exist (or is not in the caller's tenant, which the
    /// global query filter makes indistinguishable from "does not exist" - ADR-0002).</summary>
    public const string ProjectNotFound = "CpmProjectNotFound";

    /// <summary>ADR-0019/L-01 discipline: the caller has no resolvable user id
    /// (<c>ICurrentUserContext.UserId</c> is null) - a <c>CpmRun.TriggeredByUserId</c> must never
    /// be fabricated. Structurally unreachable behind <c>[Authorize]</c> in production; kept as a
    /// fail-closed guard rather than ever stamping <c>Guid.Empty</c> onto an actor field. Maps to 401.</summary>
    public const string ActorRequired = "CpmActorRequired";
}
