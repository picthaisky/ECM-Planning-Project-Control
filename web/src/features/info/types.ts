/**
 * Wire shapes for the S3-BE-04 import pipeline (`CMPlus.Application.Import.FileImportJobDto`).
 * Property names/casing mirror the backend's camelCase JSON exactly (project-wide
 * `System.Text.Json` options + `JsonStringEnumConverter`), so `format`/`status` arrive as their
 * enum *names* (e.g. `"Xer"`, `"Succeeded"`), never numbers.
 */

/** `CMPlus.Domain.Enums.ImportFileFormat`. */
export type ImportFileFormat = 'Xer' | 'Mspdi' | 'Excel'

/** `CMPlus.Domain.Enums.ImportJobStatus` — a job is created `Pending` and transitions exactly
 * once to a terminal state (never back). Today's backend (S3-BE-04) actually resolves the file
 * synchronously and the `POST` response already carries a terminal status, but the API shape is
 * job-based (a job id you can re-`GET`) so the UI polls generically rather than assuming that. */
export type ImportJobStatus = 'Pending' | 'Succeeded' | 'Failed'

/** `POST/GET .../import/...` response body (`FileImportJobDto`). */
export interface FileImportJob {
  id: string
  projectId: string
  fileName: string
  format: ImportFileFormat
  status: ImportJobStatus
  rowsImported: number
  /** Serialized `ImportErrorDetail` (`{ code, detail }`) once `status` is `'Failed'`; `null`
   * otherwise. Parse with `parseImportErrorJson` (`./errorTranslation`) before displaying. */
  errorJson: string | null
  startedAt: string
  finishedAt: string | null
  createdByUserId: string
}

/**
 * Wire shapes for S4-FE-02's project master-data view/edit (US-4.3/4.4).
 *
 * Every `decimal`/`decimal?` backend field (money AND percent alike) is transported as a JSON
 * *string* (`CMPlus.WebApi.Json.DecimalAsStringJsonConverter`, project-wide) — never a bare
 * number, so a client can never lose precision through a JS float. All of the fields below that
 * mirror a backend `decimal`/`decimal?` are typed `string`/`string | null` for exactly that
 * reason; parse with `Number(...)` only at the point of doing arithmetic/formatting
 * (`utils/format.ts`), never compare/store as a JS number across a render.
 */

/** `CMPlus.Domain.Enums.AdvanceRecoveryMethod`. */
export type AdvanceRecoveryMethod = 'ProRata' | 'ThresholdBanded' | 'Manual'

/**
 * `ProjectDto` (`UpdateProjectCommand`'s response shape, also the intended `GET
 * /api/v1/projects/{id}` response — see `api.ts`'s `getProject` remarks on why that endpoint does
 * not exist on the real backend yet).
 */
export interface Project {
  id: string
  name: string
  code: string
  owner: string
  contractStart: string
  contractFinish: string
  bac: string
  contractValue: string
  retentionRate: string | null
  advanceRate: string | null
  retentionCapPercentage: string | null
  retentionRelease1Percentage: string
  defectsLiabilityMonths: number | null
  advanceAmountPaid: string | null
  advanceRecoveryMethod: AdvanceRecoveryMethod
  advanceRecoveryStartPct: string | null
  advanceRecoveryRatePct: string | null
  advanceRecoveryEndPct: string | null
}

/** `PUT /api/v1/projects/{projectId}` request body (`UpdateProjectRequest`) — the same field set
 * as `Project` minus `id` (route param). */
export type UpdateProjectPayload = Omit<Project, 'id'>

/**
 * The `{format}` route segment for `POST /api/v1/projects/{id}/import/{format}`
 * (`ImportController.Import`). Deliberately distinct from the wire `format` enum above (the
 * route uses lowercase short names, e.g. `'xlsx'` for the `'Excel'` enum value) since the
 * controller pattern-matches this literal string to pick a parser.
 *
 * The prototype's three Import rows (docs/ECM Planning Prototype.dc.html screen #2) each pick
 * one of these explicitly — the user's choice of row *is* the format, so there is no ambiguous
 * extension-sniffing on the frontend; `ImportUploadCard` only cross-checks the picked file's
 * extension against the row's expected one as a client-side sanity guard.
 */
export type ImportRouteFormat = 'xer' | 'mspdi' | 'xlsx'
