namespace CMPlus.Application.Features.Approval;

/// <summary>Stable <see cref="CMPlus.Domain.Common.Result"/> error codes for
/// <c>SimulateApprovalRoutingQuery</c> (S15-BE-01). Mirrors the <c>ApprovalPolicyErrorCodes</c>
/// pattern - a dedicated code even where the underlying meaning duplicates an existing one (here,
/// "project not found in this tenant"), so this feature's failures are greppable/mappable on their
/// own rather than silently borrowing another feature's code and its unrelated doc comments.
/// <see cref="ApprovalErrorCodes.PolicyGap"/>/<see cref="ApprovalErrorCodes.ContractValueNotConfigured"/>
/// are reused as-is by the simulator (not duplicated here) because they are exactly the same failure
/// a real Submit would hit with the same inputs - the simulation result IS "submission would be
/// blocked", not a distinct simulator-only error.</summary>
public static class ApprovalSimulationErrorCodes
{
    /// <summary>The supplied <c>ProjectId</c> does not exist, or belongs to another tenant - the
    /// global query filter (ADR-0002) makes the two indistinguishable, which is exactly what a
    /// cross-tenant simulate request must produce (bare 404, never leak existence). Maps to 404.</summary>
    public const string ProjectNotFound = "ApprovalSimulationProjectNotFound";
}
