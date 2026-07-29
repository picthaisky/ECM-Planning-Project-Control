namespace CMPlus.Application.Abstractions;

/// <summary>
/// Exposes the configured upload size cap to the import command handlers (S3-BE-01 DoD: "read the
/// cap from config, don't hardcode"). Implemented in Infrastructure by binding an
/// <c>Import:MaxFileSizeBytes</c> configuration section (same <c>IOptions&lt;T&gt;</c> pattern as
/// <c>JwtOptions</c>) - kept behind this narrow interface rather than exposing
/// <c>IOptions&lt;ImportOptions&gt;</c> directly to Application so Application never takes a
/// dependency on <c>Microsoft.Extensions.Options</c>/configuration wiring (ADR-0001).
/// </summary>
public interface IImportOptionsProvider
{
    /// <summary>Applies uniformly to every import format (XER/MSPDI/Excel) - checked by the command
    /// handler before any parser is invoked, so an oversized file is rejected before parsing begins.</summary>
    long MaxFileSizeBytes { get; }

    /// <summary>Excel-only (S3-QA-02 finding): bounds the *uncompressed* size a `.xlsx` upload may
    /// expand to, checked from the ZIP central directory before any entry is decompressed - defends
    /// against a small, highly-compressed file exhausting memory once expanded. XER/MSPDI are read
    /// as plain text/XML with no compression layer, so this does not apply to them.</summary>
    long MaxDecompressedSizeBytes { get; }

    /// <summary>Sprint 3 security review finding H-01: an upper bound on how many rows a single
    /// XER table (<c>PROJWBS</c>/<c>TASK</c>/<c>TASKPRED</c>) may contain, checked immediately after
    /// tokenisation and before any graph (WBS tree, activity, relation) is constructed from those
    /// rows. A byte-size cap alone (<see cref="MaxFileSizeBytes"/>) bounds upload size, not parse
    /// *work* - a file well under the byte cap can still contain enough rows to drive an O(n·k)
    /// (or worse, if a future change reintroduces one) construction step into minutes/hours of CPU.
    /// This cap is deliberately generous relative to the documented performance target (10,000
    /// activities/15,000 relations) so no legitimate file is ever affected by it.</summary>
    long MaxEntityCount { get; }
}
