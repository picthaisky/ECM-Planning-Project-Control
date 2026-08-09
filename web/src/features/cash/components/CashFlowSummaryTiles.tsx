import { StatTile } from '../../../components'
import type { StatTileTone } from '../../../components'
import { formatMoneyMillions } from '../../../utils/format'
import { describeActualCostEntryCount, describeReceiptsUnavailable, toneForSign } from '../cashFlowSelectors'
import type { MetricTone } from '../cashFlowSelectors'
import type { CashFlowResponseDto } from '../types'

export interface CashFlowSummaryTilesProps {
  cashFlow: CashFlowResponseDto
}

const TONE_TO_STAT_TILE_TONE: Record<MetricTone, StatTileTone> = {
  neutral: 'neutral',
  success: 'success',
  danger: 'danger',
}

/**
 * S8-FE-02's "สรุปการเงินสะสม" tile row (prototype screen #6). Of the prototype's literal 4 tiles
 * (รับเงินสะสม / จ่ายจริงสะสม (AC) / Retention สะสม (5%) / Net Cash Position), only 2 have any real
 * backing data today — **รับเงินสะสม** needs `PaymentCertificate`/`ProjectFinanceLedger` (Sprint 9,
 * `CashFlowResponseDto.Receipts.IsAvailable === false` today) and **Retention สะสม** has no backing
 * field in the response at all (same Sprint-9 dependency) — both rendered as explicit "not available
 * yet" tiles, never a fabricated number.
 *
 * **AC label deviation from the prototype, deliberate (ADR-0013 §5's UI-copy note):** the prototype's
 * literal text is "จ่ายจริงสะสม (AC)" — "cumulative actually *paid*". Under ADR-0013, AC is cost
 * *incurred* on an accrual basis, not cash paid (paid lags incurred by a credit cycle and would
 * flatter CPI) — so this tile reads **"ต้นทุนเกิดขึ้นจริงสะสม (AC)"** instead, the corrected label
 * `actual-cost.md` §5 specifies verbatim. Same considered-deviation precedent as `features/evm/
 * components/EvmMetricsGrid.tsx`'s TCPI tile (documented there, not a silent drift from the mock).
 *
 * **Receipts vs AC visual separation (ADR-0013 §5 / this sprint's hard UI requirement):** two
 * separate tiles, two different tones (AC stays `danger`/red — same tone `EvmMetricsGrid`'s AC tile
 * already uses, matching the prototype's own literal color on that tile), and the note paragraph
 * below the grid states outright that the two must never be combined except inside Net Cash Position
 * — the one legitimate join (`actual-cost.md` §5).
 */
export function CashFlowSummaryTiles({ cashFlow }: CashFlowSummaryTilesProps) {
  const { receipts } = cashFlow

  return (
    <div>
      <div className="grid grid-cols-2 gap-3 sm:grid-cols-4" data-testid="cash-flow-summary-tiles">
        <StatTile
          label="รับเงินสะสม"
          value={receipts.isAvailable && receipts.cumulative !== null ? formatMoneyMillions(receipts.cumulative) : undefined}
          caption={
            receipts.isAvailable
              ? 'จากใบรับรองผลงาน (Payment Certificate)'
              : describeReceiptsUnavailable(receipts.unavailableReason)
          }
          tone="neutral"
        />
        <StatTile
          label="ต้นทุนเกิดขึ้นจริงสะสม (AC)"
          value={formatMoneyMillions(cashFlow.acCumulative)}
          caption={describeActualCostEntryCount(cashFlow.acCumulative, cashFlow.actualCostEntryCount)}
          tone="danger"
        />
        <StatTile
          label="Retention สะสม"
          value={undefined}
          caption="ยังไม่พร้อมใช้งาน — รอ ProjectFinanceLedger (Sprint 9)"
          tone="neutral"
        />
        <StatTile
          label="Net Cash Position"
          value={cashFlow.netCashPosition !== null ? formatMoneyMillions(cashFlow.netCashPosition) : undefined}
          caption={
            cashFlow.netCashPosition !== null
              ? 'รับเงินสะสม − ต้นทุนเกิดขึ้นจริงสะสม'
              : 'รับเงินสะสม − ต้นทุนเกิดขึ้นจริงสะสม (รอข้อมูลรับเงิน)'
          }
          tone={TONE_TO_STAT_TILE_TONE[toneForSign(cashFlow.netCashPosition)]}
        />
      </div>

      <p className="mt-3 rounded-card border border-border bg-bg px-3 py-2 text-[10.5px] leading-snug text-text-muted">
        <span className="font-semibold text-navy">รับเงินสะสม</span> (ใบรับรองผลงาน) และ{' '}
        <span className="font-semibold text-danger">ต้นทุนเกิดขึ้นจริงสะสม (AC)</span> (ต้นทุนที่เกิดขึ้นจริง)
        เป็นบัญชีคนละชุดกัน — ห้ามนำมารวมหรือใช้แทนกันโดยตรง ตัวเลขทั้งสองจะถูกนำมาหักลบกันเฉพาะใน{' '}
        <span className="font-semibold text-navy">Net Cash Position</span> เท่านั้น
      </p>
    </div>
  )
}
