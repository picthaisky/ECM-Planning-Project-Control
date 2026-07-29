namespace CMPlus.Application.Features.Projects;

/// <summary>Stable <see cref="CMPlus.Domain.Common.Result"/> error codes for the Project master-data
/// feature (S4-BE-02). Mirrors the <c>ImportErrorCodes</c>/<c>WbsErrorCodes</c> pattern.</summary>
public static class ProjectErrorCodes
{
    /// <summary>The requested project does not exist (or is not in the caller's tenant, which the
    /// global query filter makes indistinguishable from "does not exist" - ADR-0002).</summary>
    public const string NotFound = "ProjectNotFound";
}
