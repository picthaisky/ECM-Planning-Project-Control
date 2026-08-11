namespace CMPlus.Application.Abstractions;

/// <summary>
/// Exposes the configured photo upload size cap to <c>UploadPhotoCommandHandler</c> (S12-BE-01
/// DoD: "enforce a size limit"), same <c>IOptions&lt;T&gt;</c>-behind-a-narrow-interface pattern as
/// <see cref="IImportOptionsProvider"/> - kept out of Application directly so Application never
/// takes a dependency on <c>Microsoft.Extensions.Options</c>/configuration wiring (ADR-0001).
/// </summary>
public interface IPhotoOptionsProvider
{
    /// <summary>Checked by <c>ProjectPhotosController</c> against the multipart part's declared
    /// <c>IFormFile.Length</c> BEFORE any byte of the upload is copied into memory (S12-BE-01 DoD:
    /// "enforce it before buffering the whole thing into memory") - mirrors
    /// <c>ImportController</c>'s identical pre-buffering check for the schedule/Excel import path.
    /// The command handler re-checks the same cap against the declared length it is handed as
    /// defense-in-depth, the same layered-check shape <c>ImportScheduleFileCommandHandler</c> already
    /// uses.</summary>
    long MaxFileSizeBytes { get; }
}
