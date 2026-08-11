namespace CMPlus.Application.Features.Projects;

/// <summary>Stable <see cref="CMPlus.Domain.Common.Result"/> error codes for the Project master-data
/// feature (S4-BE-02). Mirrors the <c>ImportErrorCodes</c>/<c>WbsErrorCodes</c> pattern.</summary>
public static class ProjectErrorCodes
{
    /// <summary>The requested project does not exist (or is not in the caller's tenant, which the
    /// global query filter makes indistinguishable from "does not exist" - ADR-0002).</summary>
    public const string NotFound = "ProjectNotFound";

    /// <summary>Someone else changed this project between the read and the write. Reachable only
    /// since <c>Project.RowVersion</c> was added for S10-SEC-01 finding H-03 - before that, the
    /// concurrent write silently won and the other edit was lost. Maps to 409; the caller should
    /// re-read and retry.</summary>
    public const string ConcurrencyConflict = "ProjectConcurrencyConflict";

    /// <summary>ADR-0017: BAC is derived (<c>OriginalBac</c> + approved VOs) once any Variation Order
    /// has been approved, so a direct edit is refused rather than being silently ignored by every EVM
    /// figure (S10-SEC-02 finding M-02) or retroactively rewriting published history. Maps to 409 -
    /// the request is well-formed, it conflicts with the project's current state.</summary>
    public const string BacLockedByApprovedVariationOrders = "ProjectBacLockedByApprovedVariationOrders";
}
