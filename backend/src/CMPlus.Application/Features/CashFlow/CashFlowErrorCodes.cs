namespace CMPlus.Application.Features.CashFlow;

/// <summary>Stable <see cref="CMPlus.Domain.Common.Result"/> error codes for the Cash Flow feature
/// (S8-BE-01). Mirrors the per-feature-own-code convention already established by
/// <c>EvmErrorCodes</c>/<c>WbsErrorCodes</c>/etc.</summary>
public static class CashFlowErrorCodes
{
    /// <summary>The requested project does not exist (or is not in the caller's tenant, which the
    /// global query filter makes indistinguishable from "does not exist" - ADR-0002).</summary>
    public const string ProjectNotFound = "CashFlowProjectNotFound";

    /// <summary>`?from=` was later than the effective data date (`?dataDate=` or, if omitted,
    /// `Project.DataDate`) - rejected rather than silently returning a chart with no period bars,
    /// the same "an inverted range is a caller bug, don't answer it with a plausible-looking empty
    /// result" reasoning as <c>EvmErrorCodes.InvalidSnapshotRange</c>.</summary>
    public const string InvalidRange = "CashFlowInvalidRange";
}
