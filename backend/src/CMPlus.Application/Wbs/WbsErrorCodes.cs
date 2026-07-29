namespace CMPlus.Application.Wbs;

/// <summary>Stable <see cref="CMPlus.Domain.Common.Result"/> error codes for the WBS tree read
/// (S4-BE-01). Mirrors the <c>ImportErrorCodes</c>/<c>ApprovalErrorCodes</c> pattern so
/// <c>ResultProblemMapper</c> (WebApi) maps to a specific RFC 7807 <c>type</c>/<c>status</c>.</summary>
public static class WbsErrorCodes
{
    /// <summary>The requested project does not exist (or is not in the caller's tenant, which the
    /// global query filter makes indistinguishable from "does not exist" - ADR-0002).</summary>
    public const string ProjectNotFound = "WbsProjectNotFound";

    /// <summary>S4-BE-05: the requested WBS node does not exist, is not in the caller's tenant
    /// (ADR-0002), or does not belong to the project named in the route.</summary>
    public const string NodeNotFound = "WbsNodeNotFound";
}
