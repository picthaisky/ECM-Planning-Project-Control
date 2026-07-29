namespace CMPlus.Infrastructure.Import;

/// <summary>
/// Bound from the <c>Import</c> configuration section (S3-BE-01 DoD: "read the cap from config,
/// don't hardcode"). Applies uniformly to every import format (XER/MSPDI/Excel) - one cap, checked
/// once in the command handler before any parser runs.
/// </summary>
public sealed class ImportOptions
{
    public const string SectionName = "Import";

    /// <summary>Default 50 MiB - generous for a 10,000-activity/15,000-relation XER/MSPDI export
    /// (both plain text), while still bounding worst-case memory use for an oversized/malicious
    /// upload.</summary>
    public long MaxFileSizeBytes { get; set; } = 50 * 1024 * 1024;

    /// <summary>Excel-only (S3-QA-02 finding): `.xlsx` is itself a zip container, so
    /// <see cref="MaxFileSizeBytes"/> alone only bounds the *compressed* upload - a maliciously
    /// crafted file can compress at a ~1000:1 ratio (empirically verified against the actual EPPlus
    /// version this project references) and sail through that cap while still exhausting memory once
    /// decompressed. This cap is checked against the ZIP central directory's declared uncompressed
    /// entry sizes - which cannot themselves be spoofed to be *smaller* than the real decompressed
    /// bytes without corrupting the archive - before <c>ExcelPackage</c> ever decompresses anything.
    /// Default 250 MiB: generous headroom over a legitimate low-compression-ratio 50 MiB upload,
    /// while still bounding worst-case memory and reliably catching a zip-bomb-style ratio.</summary>
    public long MaxDecompressedSizeBytes { get; set; } = 250 * 1024 * 1024;

    /// <summary>Sprint 3 security review finding H-01: caps how many rows any single XER table
    /// (<c>PROJWBS</c>/<c>TASK</c>/<c>TASKPRED</c>) may contain, checked right after tokenisation and
    /// before any graph is built from those rows - a byte-size cap alone does not bound parse work,
    /// only upload size. Default 50,000: comfortably above the documented performance target
    /// (10,000 activities/15,000 relations, docs/10 §6 S3-DB-01) - real files never approach this -
    /// while still stopping the review's demonstrated attack (~750,000 PROJWBS rows in a ~24 MB file,
    /// well under <see cref="MaxFileSizeBytes"/>) long before graph construction begins.</summary>
    public long MaxEntityCount { get; set; } = 50_000;
}
