namespace CMPlus.Application.Features.ActualCosts;

/// <summary>Stable <see cref="CMPlus.Domain.Common.Result"/> error codes for the Actual Cost
/// feature (actual-cost.md, ADR-0013). Mirrors the <c>EvmErrorCodes</c>/<c>ProgressErrorCodes</c>
/// per-feature-own-code convention.</summary>
public static class ActualCostErrorCodes
{
    /// <summary>The requested project does not exist (or is not in the caller's tenant, which the
    /// global query filter makes indistinguishable from "does not exist" - ADR-0002).</summary>
    public const string ProjectNotFound = "ActualCostProjectNotFound";

    /// <summary><c>WbsNodeId</c> does not belong to this project (actual-cost.md §7.6: cost posted
    /// to a node in another project/tenant is release-blocking if it ever passes).</summary>
    public const string WbsNodeNotFound = "ActualCostWbsNodeNotFound";

    /// <summary><c>ActivityId</c> does not belong to this project.</summary>
    public const string ActivityNotFound = "ActualCostActivityNotFound";

    /// <summary><c>ReversesEntryId</c> does not reference an existing <c>ActualCostEntry</c> in
    /// this project.</summary>
    public const string ReversedEntryNotFound = "ActualCostReversedEntryNotFound";

    /// <summary>
    /// Posting into an already-closed <c>EvmPeriodSnapshot</c> period without a <c>Note</c>
    /// (actual-cost.md §6.6: allowed, but a <c>Note</c> is mandatory). Never blocks the post itself
    /// once a <c>Note</c> is supplied - <c>EvmPeriodSnapshot</c> is never touched either way.
    /// </summary>
    public const string NoteRequiredForClosedPeriod = "ActualCostNoteRequiredForClosedPeriod";
}
