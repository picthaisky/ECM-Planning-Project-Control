/**
 * Wire shapes for the S14-FE-01 Baseline feature (US-14.1/14.2), transcribed from the real source,
 * not assumed: `backend/src/CMPlus.WebApi/Controllers/Baselines/*`,
 * `backend/src/CMPlus.Application/Features/Baseline/**`.
 *
 * Every backend `decimal`/`decimal?` is transported as a JSON *string*
 * (`CMPlus.WebApi.Json.DecimalAsStringJsonConverter`, project-wide) — never a bare number; parse
 * with `Number(...)` only at the point of doing arithmetic/formatting, mirroring
 * `features/evm/types.ts`'s identical remark.
 */

/** `BaselineDto` — `POST /api/v1/projects/{projectId}/baselines` (Capture) 201 response body, and
 * the shape a real `GET /api/v1/projects/{projectId}/baselines` (list — does not exist on the real
 * backend yet, see `api.ts#listBaselines`'s remarks) would return per row. */
export interface BaselineDto {
  id: string
  projectId: string
  name: string
  isActive: boolean
  capturedAt: string
  capturedByUserId: string
  bac: string
  activityCount: number
}

/** `ActivateBaselineResultDto` — `POST .../baselines/{baselineId}/activate` 200 response body.
 * Deliberately a narrower subset of `BaselineDto` (no `name`/`capturedAt`/`bac`/`activityCount`) —
 * `useBaselines.ts#activate` merges this into the already-known `BaselineDto` row rather than
 * replacing it, so it never has to fabricate the fields this response does not carry. */
export interface ActivateBaselineResultDto {
  id: string
  projectId: string
  isActive: boolean
}

/** One activity's current-vs-baseline delta (`BaselineActivityDeltaDto`). Every `current*`/variance
 * field is `null` together when `isRemoved` is `true` — see the backend DTO's own remarks (a
 * defensive allowance, not reachable via any shipped write path today). `isCritical` is the
 * *current* schedule's criticality only (`CpmRun`-derived) — there is no baseline-side criticality
 * captured (`BaselineActivitySnapshot`'s field list does not include it), so this can describe "is
 * this activity critical right now", never "did the critical path change since baseline". See
 * `BaselineSummaryTiles.tsx`'s own remarks on why the prototype's 4th tile is not reproduced. */
export interface BaselineActivityDeltaDto {
  activityId: string
  activityCode: string | null
  name: string | null
  isRemoved: boolean
  baselinePlannedStart: string
  baselinePlannedFinish: string
  baselineDurationDays: number
  baselineBudgetCost: string
  currentPlannedStart: string | null
  currentPlannedFinish: string | null
  currentDurationDays: number | null
  currentBudgetCost: string | null
  isCritical: boolean | null
  startVarianceDays: number | null
  finishVarianceDays: number | null
  durationVarianceDays: number | null
  budgetVarianceAmount: string | null
}

/** `BaselineComparisonDto` — `GET .../baselines/compare?baselineId=` 200 response body. Three of
 * the prototype's four "เปรียบเทียบแผนปัจจุบัน vs {{ activeBlName }}" summary tiles
 * (`projectFinishVarianceDays`, `driftedActivityCount`/`totalActivityCount`, `bacVarianceAmount`) —
 * the fourth ("Critical Path เปลี่ยนเส้นทาง") is deliberately absent; see `BaselineDto`'s own
 * remarks and `BaselineSummaryTiles.tsx`. */
export interface BaselineComparisonDto {
  projectId: string
  baselineId: string
  baselineName: string
  baselineCapturedAt: string
  totalActivityCount: number
  driftedActivityCount: number
  projectFinishVarianceDays: number | null
  currentBac: string
  baselineBac: string
  bacVarianceAmount: string
  activities: BaselineActivityDeltaDto[]
}

/** `POST /api/v1/projects/{projectId}/baselines` request body (`CaptureBaselineRequest`). */
export interface CaptureBaselinePayload {
  name: string
}
