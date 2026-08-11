/**
 * Wire shapes for S12-FE-01's Photo Progress offline capture (US-12.1), transcribed from the real
 * source, not assumed: `backend/src/CMPlus.Application/Features/Photos/PhotoDto.cs`,
 * `.../Photos/Commands/UploadPhoto/UploadPhotoCommand.cs`,
 * `backend/src/CMPlus.WebApi/Controllers/Photos/ProjectPhotosController.cs`.
 *
 * `PhotoDto.FileSizeBytes` is a `long`, not a `decimal` — it is a plain JSON number (this codebase's
 * `DecimalAsStringJsonConverter`, Program.cs, only touches `decimal`/`decimal?` properties).
 */
export interface PhotoDto {
  id: string
  projectId: string
  activityId: string | null
  caption: string | null
  /** Server-computed from magic bytes, always `"image/jpeg"` or `"image/png"` — never the client's
   * declared Content-Type (`ImageSignatureValidator`/`PhotoErrorCodes.UnsupportedFormat`). */
  contentType: string
  fileSizeBytes: number
  uploadedByUserId: string
  uploadedAt: string
  capturedAt: string
}

/** There is no `GET` list endpoint for photos on the Sprint 12 backend (only `POST` upload and
 * `GET .../photos/{photoId}` for one photo's bytes — confirmed directly against
 * `ProjectPhotosController.cs`, not assumed) — S12-FE-01's DoD does not require a project-wide
 * gallery, only capture -> outbox -> sync status, so this is a scope match, not a shortcut. */
export interface UploadPhotoFields {
  activityId: string | null
  caption: string | null
  /** ISO 8601 instant; `null` lets the caller omit it (the backend accepts `capturedAt` as
   * optional `DateTimeOffset?`). */
  capturedAt: string | null
}
