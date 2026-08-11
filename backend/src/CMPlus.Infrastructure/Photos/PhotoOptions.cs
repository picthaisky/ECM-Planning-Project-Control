namespace CMPlus.Infrastructure.Photos;

/// <summary>Bound from the <c>Photos</c> configuration section (S12-BE-01 DoD: "read the cap from
/// config, don't hardcode" - same discipline as <c>ImportOptions.MaxFileSizeBytes</c>).</summary>
public sealed class PhotoOptions
{
    public const string SectionName = "Photos";

    /// <summary>Default 10 MiB: generous headroom above the ~300 KB client-side-compressed photo
    /// S12-FE-01's DoD targets (so a compliant client upload is nowhere near this cap), while still
    /// bounding worst-case memory for an upload that skips or fails client-side compression.</summary>
    public long MaxFileSizeBytes { get; set; } = 10 * 1024 * 1024;
}
