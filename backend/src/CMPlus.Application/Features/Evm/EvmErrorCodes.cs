namespace CMPlus.Application.Features.Evm;

/// <summary>Stable <see cref="CMPlus.Domain.Common.Result"/> error codes for the EVM feature
/// (S7-BE-03/05). Mirrors the <c>CpmErrorCodes</c>/<c>GanttErrorCodes</c> per-feature-own-code
/// convention.</summary>
public static class EvmErrorCodes
{
    /// <summary>The requested project does not exist (or is not in the caller's tenant, which the
    /// global query filter makes indistinguishable from "does not exist" - ADR-0002).</summary>
    public const string ProjectNotFound = "EvmProjectNotFound";

    /// <summary>
    /// Prefix for an unrecognised `?eacVariant=` query value - matched by
    /// <c>ResultProblemMapper.ToProblemDetails</c> the same way `CpmValidationErrorCodes.CycleDetected`
    /// carries a dynamic suffix (the offending raw value), never a silent fallback to the project
    /// default (design.md §2.1: "unknown eacVariant -> 400 ... never a silent fallback").
    /// </summary>
    public const string InvalidEacVariantPrefix = "EvmInvalidEacVariant";

    /// <summary>S7-BE-05 DoD: closing the same `dataDate` twice -> 409, never a silent duplicate or a
    /// silent overwrite (the whole point of an immutable snapshot).</summary>
    public const string SnapshotAlreadyExists = "EvmSnapshotAlreadyExists";

    /// <summary>`GET .../evm/snapshots?from=&amp;to=` was called with `from` later than `to`. Rejected
    /// rather than silently returning the empty list that range would produce - an inverted range is
    /// a caller bug, and answering it with a plausible-looking empty chart hides it.</summary>
    public const string InvalidSnapshotRange = "EvmInvalidSnapshotRange";
}
