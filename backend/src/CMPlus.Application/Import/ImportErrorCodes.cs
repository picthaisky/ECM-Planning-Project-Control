namespace CMPlus.Application.Import;

/// <summary>
/// Stable <see cref="CMPlus.Domain.Common.Result"/> error codes for the import pipeline (S3-BE-01..04).
/// Mirrors the <c>ApprovalErrorCodes</c> pattern (Application.Approval) so
/// <c>ResultProblemMapper</c> (WebApi) maps each to a specific RFC 7807 <c>type</c>/<c>status</c>
/// instead of falling through to a generic 400.
/// </summary>
public static class ImportErrorCodes
{
    /// <summary>Upload exceeded the configured size cap - rejected before any parsing began.</summary>
    public const string FileTooLarge = "ImportFileTooLarge";

    /// <summary>The file did not conform to the expected format's structure (missing table, bad
    /// header row, unparsable field, etc).</summary>
    public const string MalformedFile = "ImportMalformedFile";

    /// <summary>An XER/MSPDI relation graph contained a cycle; parsing is rejected all-or-nothing
    /// (S3-BE-01 DoD) - the offending chain is in <see cref="CMPlus.Domain.Common.Result.Error"/>
    /// via the caller's error-detail payload, not this code alone.</summary>
    public const string RelationCycleDetected = "ImportRelationCycleDetected";

    /// <summary>MSPDI XML contained a DOCTYPE/external-entity declaration - rejected as an XXE
    /// attempt without ever resolving the entity (S3-BE-02 DoD, ADR-0003 consequence).</summary>
    public const string XxeRejected = "ImportXxeRejected";

    /// <summary>An Excel progress row referenced an ActivityId that does not exist in the target
    /// project/tenant.</summary>
    public const string UnknownActivity = "ImportUnknownActivity";

    /// <summary>The requested project does not exist (or is not in the caller's tenant, which the
    /// global query filter makes indistinguishable from "does not exist" - ADR-0002).</summary>
    public const string ProjectNotFound = "ImportProjectNotFound";

    /// <summary>The route's <c>{format}</c> segment was not one of <c>xer</c>/<c>mspdi</c>/<c>xlsx</c>.</summary>
    public const string UnsupportedFormat = "ImportUnsupportedFormat";

    /// <summary>The requested <c>FileImportJob</c> id does not exist in the caller's tenant/project.</summary>
    public const string JobNotFound = "ImportJobNotFound";

    /// <summary>Sprint 3 security review DoD item 2 / M-01 trigger #1: the upload's actual leading
    /// bytes do not match the format implied by the route it was posted to (e.g. an XLSX ZIP
    /// signature posted to the <c>xer</c> route) - rejected before any parser is invoked, never left
    /// for whichever parser happens to run to discover by throwing.</summary>
    public const string FormatMismatch = "ImportFormatMismatch";

    /// <summary>Sprint 3 security review finding H-01: a single XER table (<c>PROJWBS</c>/<c>TASK</c>/
    /// <c>TASKPRED</c>) contained more rows than <c>IImportOptionsProvider.MaxEntityCount</c> permits -
    /// rejected immediately after tokenisation, before any graph is constructed from the rows.</summary>
    public const string EntityCountExceeded = "ImportEntityCountExceeded";

    /// <summary>Sprint 3 security review finding M-04: the caller's role is not permitted to perform
    /// this kind of import (schedule vs progress each have their own allowed-role set - see
    /// <c>ImportController</c>'s remarks). Application/controller-layer enforcement, not UI-only.</summary>
    public const string Forbidden = "ImportForbidden";
}
