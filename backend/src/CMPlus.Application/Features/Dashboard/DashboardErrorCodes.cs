namespace CMPlus.Application.Features.Dashboard;

/// <summary>Stable <see cref="CMPlus.Domain.Common.Result"/> error codes for the Executive Dashboard
/// feature (S8-BE-02). Mirrors the per-feature-own-code convention already established by
/// <c>EvmErrorCodes</c>/<c>CashFlowErrorCodes</c>/etc.</summary>
public static class DashboardErrorCodes
{
    /// <summary>The requested project does not exist (or is not in the caller's tenant, which the
    /// global query filter makes indistinguishable from "does not exist" - ADR-0002).</summary>
    public const string ProjectNotFound = "DashboardProjectNotFound";
}
