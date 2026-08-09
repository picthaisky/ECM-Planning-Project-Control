/**
 * Wire shapes for the S8-BE-01 Cash Flow read, transcribed from the real source, not assumed:
 * `backend/src/CMPlus.Application/Features/CashFlow/Queries/GetCashFlow/CashFlowResponseDto.cs`.
 *
 * Every backend `decimal`/`decimal?` is transported as a JSON *string*
 * (`CMPlus.WebApi.Json.DecimalAsStringJsonConverter`, project-wide) — never a bare number. As with
 * `features/evm/types.ts`/`features/dashboard/types.ts`, `bac/pvCumulative/evCumulative/
 * acCumulative` are never `null` (backend `decimal`, not `decimal?`) — AC = 0.00 is a genuine
 * business fact on most of today's real projects (`ActualCostEntry` ledger empty), not missing data.
 * `netCashPosition` **is** nullable and, per ADR-0013 §5, is the *only* legitimate arithmetic join
 * between the receipts ledger and the AC ledger — see `CashFlowPage.tsx`'s remarks on keeping the two
 * visually distinct everywhere else on this screen.
 */

/**
 * `CashFlowPeriodPointDto` — one period bar's PV/EV/AC, both the period delta and the running
 * cumulative. `periodStart` is `null` only for the very first bucket when no lower bound was
 * requested (project inception). `isClosed` is `true` only when this bucket is a frozen
 * `EvmPeriodSnapshot` (ADR-0009) — the trailing bucket up to the effective data date is always
 * `false` ("live"/still-open, subject to change until that period is formally closed).
 */
export interface CashFlowPeriodPointDto {
  periodStart: string | null
  periodEnd: string
  isClosed: boolean
  pvPeriod: string
  evPeriod: string
  acPeriod: string
  pvCumulative: string
  evCumulative: string
  acCumulative: string
}

/** `CashFlowReceiptsUnavailableReason` — `CMPlus.Application.Features.CashFlow.
 * CashFlowReceiptsUnavailableReason`. Only one value exists today; typed as a union (not a bare
 * `string`) so a future added reason is a visible type-level change, not a silent one. */
export type CashFlowReceiptsUnavailableReason = 'PaymentCertificatesNotYetImplemented'

/** One period's worth of certified-payment receipts — shaped for forward compatibility with Sprint
 * 9, currently never populated (`Periods` is always `[]` while `isAvailable` is `false`). */
export interface CashFlowReceiptsPeriodPointDto {
  periodStart: string | null
  periodEnd: string
  period: string
  cumulative: string
}

/**
 * `CashFlowReceiptsDto` — ADR-0013 §5 / `actual-cost.md` §5: certified-payment receipts are a
 * separate ledger from AC and must never be merged into one "cash flow" number. Modelled as a typed
 * absence (mirrors `EacVariantResponseDto`'s "every value null with a machine-readable reason"
 * shape) rather than an omitted/empty field with no explanation — `isAvailable: false` today is a
 * **structural** fact (no `PaymentCertificate`/`ProjectFinanceLedger` exists until Sprint 9), not a
 * data-quality warning the way an empty AC series would be. This is exactly the "typed absence, not
 * a bare zero" idiom `EacNullReason` established in Sprint 7 — reused for receipts, not reinvented.
 */
export interface CashFlowReceiptsDto {
  isAvailable: boolean
  cumulative: string | null
  periods: CashFlowReceiptsPeriodPointDto[]
  unavailableReason: CashFlowReceiptsUnavailableReason | null
}

/**
 * `GET /api/v1/projects/{projectId}/cash-flow` response body (S8-BE-01). Every money figure is
 * either copied verbatim from the EVM engine (live) or an `EvmPeriodSnapshot` row (frozen), or a
 * plain subtraction between two such values — see the backend handler's own remarks ("no calculation
 * outside the EVM engine" is this feature's single non-negotiable DoD line).
 */
export interface CashFlowResponseDto {
  projectId: string
  dataDate: string
  bac: string
  pvCumulative: string
  evCumulative: string
  acCumulative: string
  actualCostEntryCount: number
  periods: CashFlowPeriodPointDto[]
  receipts: CashFlowReceiptsDto
  netCashPosition: string | null
  /** e.g. `CashFlowPeriodRestated` (`CashFlowWarningCodes`) plus whatever the EVM engine itself
   * contributes (`EarnedValueExceedsBudget`/`ActualCostIsNegative`, `EvmWarningCodes`) — never
   * silently swallowed (task instruction: "surface it rather than swallowing it"). */
  warnings: string[]
}
