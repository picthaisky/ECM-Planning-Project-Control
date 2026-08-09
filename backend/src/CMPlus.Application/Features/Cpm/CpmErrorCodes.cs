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
}
