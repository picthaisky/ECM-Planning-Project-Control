/**
 * Wire shapes for the S7-BE-03/04/05 EVM feature (US-7.1/7.2/7.4*), transcribed from the real
 * source, not assumed: `backend/src/CMPlus.Application/Features/Evm/Queries/GetEvm/EvmResponseDto.cs`,
 * `.../Queries/ListEvmSnapshots/`, `.../Commands/CloseEvmPeriod/EvmSnapshotDto.cs`,
 * `.../Features/Projects/Commands/SetEacVariantDefault/`, confirmed end-to-end against
 * `backend/tests/CMPlus.Integration.Tests/WebApi/EvmControllerTests.cs`'s real HTTP round trips.
 *
 * Every backend `decimal`/`decimal?` (money AND ratio alike) is transported as a JSON *string*
 * (`CMPlus.WebApi.Json.DecimalAsStringJsonConverter`, project-wide, confirmed in `Program.cs`) —
 * never a bare number, so a client can never lose precision through a JS float. Every field below
 * that mirrors a backend `decimal`/`decimal?` is typed `string`/`string | null` for exactly that
 * reason; parse with `Number(...)` only at the point of doing arithmetic/formatting
 * (`utils/format.ts`), never compare/store as a JS number across a render (mirrors
 * `features/wbs/types.ts`'s and `features/info/types.ts`'s identical remark).
 *
 * `EacVariant`/`EacNullReason` arrive as their enum *names* (project-wide `JsonStringEnumConverter`),
 * never numbers.
 */

/** `CMPlus.Domain.Enums.EacVariant` — all 5 engine variants. ADR-0007(d): the Sprint 7 UI only
 * ever *selects* among `UI_EAC_VARIANTS` (`./evmSelectors.ts`) — `BottomUpEtc`/`CustomPf` still
 * appear in `EvmResponseDto.variants[]` (always `computable: false` until Sprint 14 gives them an
 * input UI) and must be preserved in this union so the API response still type-checks, but no
 * Sprint 7 component may render them as a *choosable* option. */
export type EacVariant = 'CpiBased' | 'Atypical' | 'CpiSpiBased' | 'BottomUpEtc' | 'CustomPf'

/** `CMPlus.Domain.Enums.EacNullReason` — machine-readable reason a given variant is not
 * computable at the current data date (never a bare `null` with no explanation). */
export type EacNullReason =
  | 'NotStarted'
  | 'NoActualCost'
  | 'NoPlannedValue'
  | 'ZeroCpi'
  | 'ManualEtcNotSet'
  | 'CustomPfNotSet'

/** `EacVariantResponseDto` — one entry of `EvmResponseDto.variants[]`. `computable: false` always
 * pairs with `reason` set and every money/ratio field `null` — never a fabricated 0.00. */
export interface EacVariantResponseDto {
  variant: EacVariant
  performanceFactor: string | null
  etc: string | null
  eac: string | null
  vac: string | null
  computable: boolean
  reason: EacNullReason | null
}

/** `EvmResponseDto` — `GET /api/v1/projects/{projectId}/evm` response body (design.md §2.1). The
 * 12-metric table (S7-FE-01) is built from `bac/pv/ev/ac/sv/cv/spi/cpi` here plus the
 * `selectedVariant`'s own `eac/etc/vac` (from `variants[]`) and a `tcpiBac`/`tcpiEac` pick
 * (`resolveTcpi`, `./evmSelectors.ts`) — never a `%Complete` field, which this DTO does not have.
 *
 * `bac/pv/ev/ac/sv/cv` are never `null` (backend `decimal`, not `decimal?`) — `AC` is a genuine
 * business value of `0.00` today (no cost-recording entity exists yet, see this sprint's frontend
 * report), which is a *fact*, not a missing-data case, hence not nullable on the wire either.
 * `spi/cpi/tcpiBac/tcpiEac` are `decimal?` — `null` renders "—", never 0.
 */
export interface EvmResponseDto {
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
  tcpiBac: string | null
  tcpiEac: string | null
  selectedVariant: EacVariant
  variants: EacVariantResponseDto[]
  /** e.g. `EarnedValueExceedsBudget` (evm-formulas.md's edge-case table) — a data-quality signal,
   * never blocking, but must not be silently dropped. */
  warnings: string[]
}

/** `EvmSnapshotDto` — one closed, immutable reporting period (ADR-0009). Returned both by
 * `POST .../evm/snapshots` (close a period) and `GET .../evm/snapshots?from=&to=` (history, what
 * S7-FE-04's S-Curve reads for every point *before* the live data date — real backend source read
 * directly, not assumed: `CloseEvmPeriod/EvmSnapshotDto.cs`). */
export interface EvmSnapshotDto {
  snapshotId: string
  projectId: string
  dataDate: string
  bac: string
  pv: string
  ev: string
  ac: string
  eacVariant: EacVariant
  performanceFactor: string | null
  eac: string | null
  etc: string | null
  vac: string | null
  createdAt: string
}

/** One plotted point for `components/SCurveChart.tsx` — the money-relevant subset shared by both
 * `EvmSnapshotDto` (historical, pre-data-date) and `EvmResponseDto` (the live, current point). See
 * `evmSelectors.ts#buildSCurvePoints`, which is what actually assembles an array of these from both
 * sources per ADR-0009. */
export interface SCurvePoint {
  dataDate: string
  pv: string
  ev: string
  ac: string
}

/** `PUT /api/v1/projects/{projectId}/eac-default` request body (`SetEacVariantDefaultRequest`) —
 * PM/QS/Executive only server-side (ADR-0007(f)); 403 for every other role. */
export interface SetEacVariantDefaultPayload {
  variant: EacVariant
}

/** `SetEacVariantDefaultResultDto` — the `PUT .../eac-default` 200 response body. */
export interface SetEacVariantDefaultResult {
  projectId: string
  eacVariantDefault: EacVariant
}
