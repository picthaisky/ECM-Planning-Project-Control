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

/** `CMPlus.Domain.Enums.EacVariant` — kept in sync with `features/evm/types.ts`'s identical
 * definition rather than importing across feature boundaries for one type alias (this module
 * already imports `useProjectMasterData` from `features/vo/VoPage.tsx` in the other direction, so
 * cross-feature imports are an established pattern here — this one is just a plain type with
 * nothing to gain from a shared import). */
export type EacVariant = 'CpiBased' | 'Atypical' | 'CpiSpiBased' | 'BottomUpEtc' | 'CustomPf'

/**
 * S14-BE-03/ADR-0007(d): the project's EAC configuration — `EacVariantDefault`/`EacManualEtc`/
 * `EacCustomPerformanceFactor`/`EacManualEtcStaleSince`. **Deliberately not part of `Project`
 * above** even though a real `GetProjectQuery` should return all of it together — see
 * `api.ts#getProject`'s remarks: `ProjectDto` (`UpdateProjectCommand`'s response) *excludes* these
 * fields by the backend's own explicit design ("ADR-0007(c)'s own dedicated, separately-audited
 * action from Sprint 7, not part of this command" — `ProjectDto`'s own doc comment), so a `PUT
 * /api/v1/projects/{id}` response can never be used to refresh these fields. Keeping them a
 * separate type (rather than folding into `Project`) is what makes `useProjectMasterData.save`
 * safe: it can merge the base `Project` fields `updateProject` actually returns into state without
 * ever having to guess whether it should also overwrite `eacManualEtc`/`eacCustomPerformanceFactor`
 * with `undefined`.
 */
export interface ProjectEacConfig {
  eacVariantDefault: EacVariant
  eacManualEtc: string | null
  eacCustomPerformanceFactor: string | null
  /** `Project.EacManualEtcStaleSince` (domain-rules.md §5.7) — non-null means an approved VO has
   * moved BAC since `eacManualEtc` was last (re-)entered; the EVM page surfaces the same fact as
   * the `ManualEtcPredatesBacChange` warning (`features/evm/evmSelectors.ts`). Re-entering
   * `eacManualEtc` here (`setEacAdvancedInputs`) always clears it. */
  eacManualEtcStaleSince: string | null
}

/**
 * The intended full `GET /api/v1/projects/{projectId}` response shape once that endpoint exists
 * for real (see `api.ts#getProject`'s remarks) — `Project` (the general commercial-terms fields
 * S4-FE-02 already edits) plus `ProjectEacConfig` (the S14-FE-02 fields). `useProjectMasterData`
 * loads this shape; `EacAdvancedInputsCard` reads only its `ProjectEacConfig` slice.
 */
export type ProjectDetail = Project & ProjectEacConfig

/** `PUT /api/v1/projects/{projectId}/eac-advanced-inputs` request body
 * (`SetEacAdvancedInputsRequest`) — full-representation: both fields are always sent, either may
 * be `null` (see `api.ts#setEacAdvancedInputs`'s remarks on why `null` here really means "clear",
 * never "leave alone"). */
export interface SetEacAdvancedInputsPayload {
  eacManualEtc: string | null
  eacCustomPerformanceFactor: string | null
}

/** `SetEacAdvancedInputsResultDto` — the `PUT .../eac-advanced-inputs` 200 response body. */
export interface SetEacAdvancedInputsResult {
  projectId: string
  eacManualEtc: string | null
  eacCustomPerformanceFactor: string | null
  eacManualEtcStaleSince: string | null
}

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
