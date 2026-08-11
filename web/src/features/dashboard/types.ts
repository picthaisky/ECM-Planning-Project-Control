/**
 * Wire shapes for the S8-BE-02 Executive Dashboard read, transcribed from the real source, not
 * assumed: `backend/src/CMPlus.Application/Features/Dashboard/Queries/GetDashboard/DashboardResponseDto.cs`.
 *
 * Every backend `decimal`/`decimal?` is transported as a JSON *string*
 * (`CMPlus.WebApi.Json.DecimalAsStringJsonConverter`, project-wide) — never a bare number, so a
 * client can never lose precision through a JS float. `bac/pv/ev/ac/sv/cv` are never `null` (backend
 * `decimal`, not `decimal?`) — same "AC = 0.00 is a genuine business fact, not missing data" rule
 * `features/evm/types.ts` documents; `spi/cpi/performanceFactor/etc/eac/vac` are `decimal?` — `null`
 * renders "—", never 0 (mirrors `features/evm/`'s discipline throughout). `int`/`bool` fields are
 * plain JSON numbers/booleans (only `decimal`/`decimal?` go through the string converter).
 *
 * `EacVariant`/`EacNullReason` duplicate `features/evm/types.ts`'s own unions verbatim rather than
 * being imported from that feature — every feature folder in this codebase owns its own wire-shape
 * file independently (no feature currently imports another feature's `types.ts`), so this keeps
 * `features/dashboard/` fully self-contained the same way `features/evm/`, `features/wbs/` and
 * `features/gantt/` each independently define their own money-as-string conventions. Cost of
 * duplication is two small compile-time-only type aliases; see this sprint's frontend report for the
 * note to `knowledge-curator` about promoting this (and the matching Thai label dictionaries in
 * `dashboardSelectors.ts`) into a shared module once a third consumer appears.
 */

/** `CMPlus.Domain.Enums.EacVariant` — all 5 engine variants (ADR-0007). The Dashboard never lets the
 * user pick a variant (unlike the EVM screen's selector) — it always reflects
 * `Project.EacVariantDefault`, so this union exists only to type `eacVariant`/caption lookups. */
export type EacVariant = 'CpiBased' | 'Atypical' | 'CpiSpiBased' | 'BottomUpEtc' | 'CustomPf'

/** `CMPlus.Domain.Enums.EacNullReason` — machine-readable reason the project's default EAC variant
 * is not computable at the current data date. */
export type EacNullReason = 'NotStarted' | 'NoActualCost' | 'NoPlannedValue' | 'ZeroCpi' | 'ManualEtcNotSet' | 'CustomPfNotSet'

/** `DashboardWeightWarningDto` — one WBS level whose children's `WeightPercentage` values did not
 * sum to 100.00. `wbsNodeId: null` means the project's own top-level (root) siblings, otherwise the
 * parent node whose direct children are misconfigured (`WbsProgressRollupCalculator`'s own remarks).
 * Surfaced, never blocking (S8-BE-02 DoD: "น้ำหนักไม่ครบ 100 → เตือน ไม่บล็อก"). */
export interface DashboardWeightWarningDto {
  wbsNodeId: string | null
  childCount: number
  weightSum: string
}

/** `DashboardProgressRollupDto` — US-8.3's weight-based WBS progress rollup
 * (`evm-formulas.md`'s "Progress rollup (WBS tree)" rule: `Pct_parent = Σ(Pct_c·W_c) / Σ(W_c)`).
 * Deliberately not a function of `dataDate` — always reflects `Activity.ProgressPercentage`'s
 * current state, the same figure the WBS screen itself shows (`GetDashboardQueryHandler`'s remarks:
 * "must agree always", not only when `dataDate` happens to equal today). `mixedScopeWbsNodeIds` are
 * nodes that have *both* child nodes and their own direct activities — the direct activities are
 * excluded from that node's own rollup figure (only the child-subtree rollup counts;
 * `WbsProgressRollupCalculator`'s own remarks), surfaced here rather than silently dropped. */
export interface DashboardProgressRollupDto {
  progressPercentage: string
  weightWarnings: DashboardWeightWarningDto[]
  mixedScopeWbsNodeIds: string[]
}

/**
 * `GET /api/v1/projects/{projectId}/dashboard` response body (S8-BE-02) — the KPI tiles. Every
 * EVM-sourced field (everything except `progressRollup`) comes straight from
 * `EvmComputationService.ComputeAsync`'s result at `Project.EacVariantDefault` specifically — the
 * same pipeline `features/evm/`'s `GET .../evm` calls, just pre-selected to one variant server-side
 * (there is no `?eacVariant=` override on this endpoint, and no variant switcher on this screen).
 */
export interface DashboardResponseDto {
  projectId: string
  dataDate: string
  bac: string
  pv: string
  ev: string
  ac: string
  sv: string
  cv: string
  spi: string | null
  cpi: string | null
  actualCostEntryCount: number
  eacVariant: EacVariant
  performanceFactor: string | null
  etc: string | null
  eac: string | null
  vac: string | null
  eacComputable: boolean
  eacNullReason: EacNullReason | null
  progressRollup: DashboardProgressRollupDto
  /** e.g. `EarnedValueExceedsBudget`/`ActualCostIsNegative` (`EvmWarningCodes`) — a data-quality
   * signal, never blocking, but must not be silently dropped (mirrors `EvmResponseDto.warnings`). */
  warnings: string[]
}
