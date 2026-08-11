namespace CMPlus.Application.Features.Gantt;

/// <summary>Stable <see cref="CMPlus.Domain.Common.Result"/> error codes for the Gantt read
/// (S6-BE-01). Mirrors the <c>CpmErrorCodes</c>/<c>WbsErrorCodes</c> per-feature-own-code
/// convention - each feature defines its own "project not found" constant even though they all map
/// to the same generic 404 <c>ResultProblemMapper</c> entry, rather than reusing another feature's
/// error code across a feature boundary.</summary>
public static class GanttErrorCodes
{
    /// <summary>The requested project does not exist (or is not in the caller's tenant, which the
    /// global query filter makes indistinguishable from "does not exist" - ADR-0002).</summary>
    public const string ProjectNotFound = "GanttProjectNotFound";
}
