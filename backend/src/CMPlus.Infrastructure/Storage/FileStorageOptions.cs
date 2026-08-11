namespace CMPlus.Infrastructure.Storage;

/// <summary>
/// Bound from the <c>Storage</c> configuration section. Backs <see cref="LocalDiskFileStorage"/>,
/// the dev/local <c>IFileStorage</c> adapter ADR-0010 calls for ("Infrastructure provides a
/// local-disk adapter for dev and an S3-API-compatible adapter for staging/production; the
/// concrete provider adapter is written only after the Sprint 16 gate").
/// </summary>
public sealed class FileStorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>Defaults under the OS temp directory - deliberately OUTSIDE this repository, outside
    /// <c>CMPlus.WebApi</c>'s content root, and (S12-BE-01 DoD: "keep the file outside the webroot")
    /// there is no <c>wwwroot</c>/<c>UseStaticFiles()</c> anywhere in this solution at all, so no
    /// path under this root can ever become directly web-servable regardless of what it is set to.
    /// Overridden per test run by <c>CustomWebApplicationFactory</c> to a unique subfolder so
    /// parallel test runs cannot collide.</summary>
    public string LocalRootPath { get; set; } = Path.Combine(Path.GetTempPath(), "cmplus-file-storage");
}
