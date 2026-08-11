/**
 * Wire shapes for S11-FE-01's Weather Log + EOT evaluation screen (US-11.1), transcribed from the
 * real source, not assumed: `backend/src/CMPlus.Application/Features/Weather/WeatherLogDto.cs`,
 * `.../Weather/Commands/RecordWeatherLog{,Correction}/*.cs`,
 * `backend/src/CMPlus.WebApi/Controllers/Weather/WeatherLogRequests.cs`,
 * `backend/src/CMPlus.Domain/Enums/WeatherCondition.cs` / `WeatherImpact.cs` /
 * `WeatherLogEntryKind.cs`, `.../Features/Eot/EotEvaluationDto.cs`,
 * `backend/src/CMPlus.WebApi/Controllers/Eot/EvaluateEotRequest.cs`,
 * `backend/src/CMPlus.Domain/Enums/EotCriticalityBasis.cs` / `EotConfidence.cs` /
 * `EotExclusionReason.cs`.
 *
 * Every backend `decimal`/`decimal?` is a JSON *string* (`DecimalAsStringJsonConverter`, project-
 * wide) — `rainfallMm`/`hoursLost`/`countableDays`/`unclaimedFractionalHours` below are typed
 * `string`/`string | null` for that reason (mirrors `features/vo/types.ts`'s identical remark).
 * Every enum arrives as its *name* (project-wide `JsonStringEnumConverter`), never a number.
 */

// ---------------------------------------------------------------------------------------------
// DailyWeatherLog (domain-rules.md weather-eot §8.1)
// ---------------------------------------------------------------------------------------------

export type WeatherCondition =
  | 'Clear'
  | 'Cloudy'
  | 'LightRain'
  | 'ModerateRain'
  | 'HeavyRain'
  | 'Storm'
  | 'Flood'
  | 'Other'

/** `WorkStoppage` is *derived* from this server-side, never independently stored (§3.4). */
export type WeatherImpact = 'NoImpact' | 'PartialStoppage' | 'FullStoppage'

/** The correction chain (§8.2). `Original` carries neither `correctsWeatherLogId` nor
 * `correctionReason`; `Correction`/`Retraction` require both. */
export type WeatherLogEntryKind = 'Original' | 'Correction' | 'Retraction'

/** `WeatherLogDto` — the shared response shape for every S11-BE-01 write and the real, live
 * `GET /api/v1/projects/{projectId}/weather-logs` read. The read returns the **full history**
 * (every `Original`/`Correction`/`Retraction` row, newest `logDate` first, no pagination) — it is
 * not pre-filtered to the effective set $\mathcal{L}^{eff}$; see `weatherChain.ts` for computing
 * which rows are currently in force, exactly per domain-rules.md §8.2's formula. */
export interface WeatherLogDto {
  id: string
  projectId: string
  logDate: string
  condition: WeatherCondition
  conditionNote: string | null
  rainfallMm: string | null
  impact: WeatherImpact
  impactNote: string | null
  hoursLost: string | null
  workStoppage: boolean
  entryKind: WeatherLogEntryKind
  correctsWeatherLogId: string | null
  correctionReason: string | null
  affectedActivityIds: string[]
  recordedByUserId: string
  recordedAt: string
}

/** `RecordWeatherLogRequest` — `POST /api/v1/projects/{projectId}/weather-logs` (creates
 * `EntryKind = Original` only). */
export interface RecordWeatherLogPayload {
  logDate: string
  condition: WeatherCondition
  conditionNote: string | null
  rainfallMm: string | null
  impact: WeatherImpact
  impactNote: string | null
  hoursLost: string | null
  affectedActivityIds: string[]
}

/** `RecordWeatherLogCorrectionRequest` — `POST .../weather-logs/{logId}/corrections`.
 * `correctsWeatherLogId` is deliberately **not** a field here — the backend always takes it from
 * the route (§8.3), never the body, so a client cannot aim a correction at a different row than
 * the one it is currently viewing. A correction **replaces**, it does not patch (§8.2 rule 6): every
 * field below governs completely, so the caller must send the target's current values for anything
 * left unchanged. */
export interface RecordWeatherLogCorrectionPayload {
  entryKind: Extract<WeatherLogEntryKind, 'Correction' | 'Retraction'>
  correctionReason: string
  logDate: string
  condition: WeatherCondition
  conditionNote: string | null
  rainfallMm: string | null
  impact: WeatherImpact
  impactNote: string | null
  hoursLost: string | null
  affectedActivityIds: string[]
}

// ---------------------------------------------------------------------------------------------
// EotEvaluation (domain-rules.md weather-eot §2, §4, §5, §8.5) — ADR-0019, ADR-0020
// ---------------------------------------------------------------------------------------------

/** §4.4's degraded-mode ladder. `Contemporaneous` = a `CpmRun` existed at/before every countable
 * day; `Mixed`/`Retrospective` fall back to the earliest available run for some/all days. */
export type EotCriticalityBasis = 'Contemporaneous' | 'Mixed' | 'Retrospective'

/** Whether `criticalityBasis` rests on real contemporaneous schedule history or a fallback —
 * `Provisional` "must never be presented as a substantiated figure" (§4.4). */
export type EotConfidence = 'Substantiated' | 'Provisional'

/** §3: why one weather-log source day did not become a countable EOT day — "the evaluator never
 * silently drops a row" (fixture W-09). Deliberately excludes "superseded/retracted" and
 * "outside the window" — both are filtered before the evaluator considers a row at all, see
 * `EotExclusionReason.cs`'s own remarks. */
export type EotExclusionReason =
  | 'NoAffectedActivity'
  | 'NonWorkingDay'
  | 'CalendarHoliday'
  | 'BelowHoursThreshold'
  | 'BelowRainfallThreshold'
  | 'RainfallNotMeasured'
  | 'ActivityAlreadyComplete'
  | 'ActivityNotYetScheduled'
  | 'NoStoppageRecorded'

/** One governing `CpmRun` window's own as-scheduled/impacted delta (§5.1's windows-TIA method —
 * summed, never sliced, across runs). */
export interface EotEvaluationRunDto {
  cpmRunId: string
  windowFrom: string
  windowTo: string
  asScheduledDurationDays: number
  impactedDurationDays: number
  deltaDays: number
}

/** One weather-log entry considered by the evaluation — `exclusionReason: null` iff the entry
 * contributed countable days. */
export interface EotEvaluationSourceDto {
  dailyWeatherLogId: string
  countableDays: string
  exclusionReason: EotExclusionReason | null
}

/** One affected activity's contribution (§5.4 "explainability" — the DoD's "อธิบายได้ว่าอ้างกิจกรรมใด").
 * **Neither `indicativeEotDays` nor `marginalEotDays` is required to sum to the evaluation's own
 * `eotEligibleDays`** — the network figure always governs; render that caveat on screen (§5.4). */
export interface EotEvaluationDriverDto {
  cpmRunId: string
  activityId: string
  activityCode: string
  activityName: string
  stoppageDays: number
  totalFloatAtRun: number
  wasCriticalAtRun: boolean
  isOnImpactedCriticalPath: boolean
  indicativeEotDays: number
  marginalEotDays: number
  remainingFloatAfter: number
  unclaimedFractionalHours: string | null
}

/**
 * `EotEvaluationDto` — `POST /api/v1/projects/{projectId}/eot-evaluations` response (S11-BE-02).
 * There is deliberately **no** `GET` for this resource: re-evaluating is always a new `POST`
 * (domain-rules.md §8.5 rule 3, "the remedy is always a new evaluation record"), so this screen has
 * no "load the last evaluation" affordance — only "run one now".
 *
 * `eotEligibleDays` is named exactly as the domain document requires (§2.2: **a schedule fact, not
 * an entitlement** — ADR-0020) — never relabelled to anything implying entitlement anywhere in this
 * module; see `EotEvaluationPanel.tsx` for the on-screen relabelling this type's own field name does
 * NOT carry (the wire field stays `eotEligibleDays`; only the *display* copy changes).
 * `concurrencyAssessed`/`entitlementBasisAssessed` are hard-coded `false` on every Sprint 11
 * evaluation (§2.2, §7.2) — rendered as permanent disclosures, never conditionally hidden.
 */
export interface EotEvaluationDto {
  id: string
  projectId: string
  windowStart: string
  windowEnd: string
  evaluatedAt: string
  evaluatedByUserId: string
  criticalityBasis: EotCriticalityBasis
  confidence: EotConfidence
  asScheduledDurationDays: number
  impactedDurationDays: number
  eotEligibleDays: number
  countableStoppageDayCount: number
  distinctCountableDateCount: number
  unattributedStoppageDayCount: number
  concurrencyAssessed: boolean
  entitlementBasisAssessed: boolean
  latestNoticeDate: string | null
  noticeWindowExpired: boolean | null
  runs: EotEvaluationRunDto[]
  sources: EotEvaluationSourceDto[]
  drivers: EotEvaluationDriverDto[]
}

/** `EvaluateEotRequest` — `POST /api/v1/projects/{projectId}/eot-evaluations` body. Both fields
 * `null` (the default) lets the backend use the project's own `ContractStart`/data date (§1). */
export interface EvaluateEotPayload {
  windowStart: string | null
  windowEnd: string | null
}
