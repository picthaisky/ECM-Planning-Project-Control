/**
 * Wire shapes for S12-FE-02's Man/Equipment screen (US-12.2), transcribed from the real source, not
 * assumed: `backend/src/CMPlus.Application/Features/Manpower/ManpowerLogDto.cs`,
 * `.../Manpower/Commands/RecordManpowerLog{,Correction}/*.cs`,
 * `backend/src/CMPlus.WebApi/Controllers/Manpower/ManpowerLogRequests.cs`,
 * `.../Manpower/Queries/GetProductivityIndex/ProductivityIndexResponseDto.cs`,
 * `.../Application/Services/Manpower/ProductivityIndexModels.cs`,
 * `backend/src/CMPlus.Domain/Enums/{Shift,LabourType,ManpowerLogEntryKind}.cs`.
 *
 * Every backend `decimal`/`decimal?` is a JSON *string* (project-wide `DecimalAsStringJsonConverter`)
 * — `manHours`/`overtimeHours`/`equipmentOperatingHours`/`equipmentStandbyHours`/`productivityIndex`/
 * `earnedManHours`/`actualManHoursInScope`/`actualManHoursTotal`/`excludedManHours`/
 * `coveragePercentage`/`manningRatio` below are typed `string`/`string | null` for that reason,
 * mirroring `features/weather/types.ts`'s identical documented convention. `workerCount`/
 * `equipmentCount`/`logEntryCount`/`actualWorkerCount`/`plannedWorkerCount` are plain `int`s, so
 * plain JSON numbers. Every enum arrives as its *name* (project-wide `JsonStringEnumConverter`).
 */

export type Shift = 'Day' | 'Night'
export type LabourType = 'OwnDirect' | 'Subcontract' | 'Hired'
export type ManpowerLogEntryKind = 'Original' | 'Correction' | 'Retraction'

/** domain-rules.md (manpower-equipment) §5.4/§5.7 — the exhaustive, closed set of reasons a null
 * `productivityIndex` can occur. Every member needs distinct Thai copy (`maneqLabels.ts`) — none of
 * them is "0 wearing a different hat" (§5.7(c): earned-hours = 0 with hours logged is a **defined**
 * `0.00`, never one of these reasons). */
export type PiNullReason =
  | 'NotReported'
  | 'NoActualManHours'
  | 'NoBudgetManHours'
  | 'NoProgressInPeriod'
  | 'NoMatchingBudgetedScope'
  | 'ActivitiesNotCategorised'

/** §5.4/§5.7/§7.4 — advisory-only; never changes `productivityIndex`'s value or its colour band
 * (§5.3: "a genuine 3.5 exists and hiding it would be worse than explaining it"). */
export type PiDataQualityWarning =
  | 'ImplausiblePi'
  | 'ProgressWithoutManHours'
  | 'UnbudgetedLabourHours'
  | 'NegativeEarnedHours'
  | 'CircularEarningBasisRisk'

/** `ManpowerLogDto` — the response shape for both write endpoints (Original + Correction/Retraction). */
export interface ManpowerLogDto {
  id: string
  projectId: string
  logDate: string
  shift: Shift
  workCategoryId: string
  wbsNodeId: string | null
  activityId: string | null
  labourType: LabourType
  subcontractorRef: string | null
  workerCount: number
  manHours: string
  overtimeHours: string
  manHoursDerived: boolean
  equipmentCount: number
  equipmentOperatingHours: string
  equipmentStandbyHours: string
  workDescription: string | null
  relatedWeatherLogId: string | null
  recordedByUserId: string
  recordedAt: string
  entryKind: ManpowerLogEntryKind
  correctsLogId: string | null
  correctionReason: string | null
  allowDuplicateOverride: boolean
}

/** `RecordManpowerLogRequest` — `POST /api/v1/projects/{projectId}/manpower-logs` (Original only). */
export interface RecordManpowerLogPayload {
  logDate: string
  shift: Shift
  workCategoryId: string
  wbsNodeId: string | null
  activityId: string | null
  labourType: LabourType
  subcontractorRef: string | null
  workerCount: number
  manHours: string
  overtimeHours: string
  manHoursDerived: boolean
  equipmentCount: number
  equipmentOperatingHours: string
  equipmentStandbyHours: string
  workDescription: string | null
  relatedWeatherLogId: string | null
  allowDuplicate: boolean
}

/** `RecordManpowerLogCorrectionRequest` — `POST .../manpower-logs/{logId}/corrections`.
 * `correctsLogId` is deliberately not a field — the backend always takes it from the route
 * (domain-rules.md §4.7), same discipline as `features/weather`'s correction payload. */
export interface RecordManpowerLogCorrectionPayload {
  entryKind: Extract<ManpowerLogEntryKind, 'Correction' | 'Retraction'>
  correctionReason: string
  logDate: string
  shift: Shift
  workCategoryId: string
  wbsNodeId: string | null
  activityId: string | null
  labourType: LabourType
  subcontractorRef: string | null
  workerCount: number
  manHours: string
  overtimeHours: string
  manHoursDerived: boolean
  equipmentCount: number
  equipmentOperatingHours: string
  equipmentStandbyHours: string
  workDescription: string | null
  relatedWeatherLogId: string | null
}

/**
 * `ProductivityIndexResponseDto` — `GET .../manpower-logs/productivity-index`. `manningRatio`/
 * `actualWorkerCount`/`plannedWorkerCount` are populated **only** when the query window is exactly
 * one calendar day (`GetProductivityIndexQueryHandler.cs`'s own rule) — `null` otherwise, never a
 * stale/reused value from a different window.
 *
 * **`productivityIndex` and `manningRatio` are two distinct fields for a reason (domain-rules.md
 * §5.1, fixture M-02) — `manningRatio` is a staffing-compliance ratio, never a substitute for or a
 * synonym of Productivity Index. Never bind `manningRatio`'s value to a UI label that says
 * "Productivity" anywhere in this feature.**
 */
export interface ProductivityIndexResponseDto {
  projectId: string
  wbsNodeId: string | null
  activityId: string | null
  from: string | null
  to: string
  productivityIndex: string | null
  productivityIndexNullReason: PiNullReason | null
  earnedManHours: string
  actualManHoursInScope: string
  actualManHoursTotal: string
  excludedManHours: string
  coveragePercentage: string
  logEntryCount: number
  warnings: PiDataQualityWarning[]
  manningRatio: string | null
  actualWorkerCount: number | null
  plannedWorkerCount: number | null
}
