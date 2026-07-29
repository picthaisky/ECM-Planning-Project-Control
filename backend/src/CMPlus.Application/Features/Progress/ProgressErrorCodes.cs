namespace CMPlus.Application.Features.Progress;

/// <summary>Stable <see cref="CMPlus.Domain.Common.Result"/> error codes for the Progress feature
/// (S4-BE-03). Mirrors the <c>ImportErrorCodes</c>/<c>WbsErrorCodes</c> pattern.</summary>
public static class ProgressErrorCodes
{
    /// <summary>The requested project does not exist (or is not in the caller's tenant, which the
    /// global query filter makes indistinguishable from "does not exist" - ADR-0002).</summary>
    public const string ProjectNotFound = "ProgressProjectNotFound";

    /// <summary>One or more <c>ActivityId</c> values in the batch do not belong to this project -
    /// the whole batch is rejected (all-or-nothing, S4-BE-03 DoD), not just the offending rows.</summary>
    public const string UnknownActivity = "ProgressUnknownActivity";
}
