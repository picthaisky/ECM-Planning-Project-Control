/**
 * Wire shapes for S4-FE-03's WBS & Activity screen (US-4.1, US-4.5).
 *
 * As with `features/info/types.ts`, every backend `decimal`/`decimal?` is a JSON *string*
 * (`DecimalAsStringJsonConverter`, project-wide) — `weightPercentage`/percentage fields below are
 * typed `string` for that reason.
 */

/** `WbsTreeNodeDto` (`GetWbsTreeQuery`, S4-BE-01) — one WBS node plus its nested children.
 * `activityCount` is a rolled-up count only; the activities themselves are not embedded (lazy/
 * paginated child-loading, per that query's own doc comment) — see `useNodeActivities.ts`. */
export interface WbsTreeNodeDto {
  id: string
  parentWbsNodeId: string | null
  code: string
  title: string
  weightPercentage: string
  activityCount: number
  children: WbsTreeNodeDto[]
}

/** `WbsTreeDto` — `GET /api/v1/projects/{projectId}/wbs-tree` response body. */
export interface WbsTreeDto {
  projectId: string
  rootNodes: WbsTreeNodeDto[]
}

/**
 * One activity available for progress entry under a given WBS node.
 *
 * There is no real backend endpoint for this yet — see `api.ts`'s `getNodeActivities` remarks —
 * the natural complement `GET
 * /api/v1/projects/{projectId}/wbs-nodes/{wbsNodeId}/activities` this type models does not exist
 * on the live API. `currentProgressPercentage` is what `useBatchProgressForm.ts` compares a new
 * entry against to decide whether the "ยืนยันการปรับลดความคืบหน้า" confirmation is required.
 */
export interface ActivityForProgress {
  id: string
  activityCode: string
  name: string
  currentProgressPercentage: string
}

/** `BatchRecordProgressEntryRequest` — one row of `POST .../progress/batch`. */
export interface BatchProgressEntryRequest {
  activityId: string
  progressPercentage: string
  actualQuantity: string | null
}

/** `BatchRecordProgressRequest` — the whole batch (one `periodEndDate`, N entries). */
export interface BatchProgressRequest {
  periodEndDate: string
  entries: BatchProgressEntryRequest[]
}

/** `BatchRecordProgressResultDto` — `{ entriesRecorded }`, the batch's own audit-summary count. */
export interface BatchProgressResult {
  entriesRecorded: number
}
