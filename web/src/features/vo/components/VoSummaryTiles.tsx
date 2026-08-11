import { useMemo } from 'react'
import { StatTile } from '../../../components'
import { formatMoney } from '../../../utils/format'
import type { VariationOrderDto } from '../types'

export interface VoSummaryTilesProps {
  variationOrders: VariationOrderDto[]
  /** `Project.BAC`, current. `null` while unknown/unloaded — rendered as an honest "—" rather than
   * a guessed figure (see `VoPage.tsx`'s remarks: no live endpoint reliably returns this to every
   * VO-screen role today). */
  currentBac: number | null
  bacLoadState: 'loading' | 'ready' | 'error'
}

/** Prototype screen #9's three summary tiles (`docs/ECM Planning Prototype.dc.html` ~line 385-388):
 * "อนุมัติแล้ว" (sum + count of `Approved`), "รออนุมัติ" (sum + count of `PendingApproval`), "BAC
 * ปรับปรุง (รวม VO อนุมัติ)". The first two are always derivable client-side from the real, live VO
 * list (net-signed sums, ADR-0015's own N-1 convention) — the third needs `Project.BAC`, which this
 * component never fetches itself; it renders whatever `currentBac` the caller could obtain. */
export function VoSummaryTiles({ variationOrders, currentBac, bacLoadState }: VoSummaryTilesProps) {
  const { approvedSum, approvedCount, pendingSum, pendingCount } = useMemo(() => {
    let approvedSumAcc = 0
    let approvedCountAcc = 0
    let pendingSumAcc = 0
    let pendingCountAcc = 0

    for (const vo of variationOrders) {
      const amount = Number(vo.amount)
      if (vo.status === 'Approved') {
        approvedSumAcc += amount
        approvedCountAcc += 1
      } else if (vo.status === 'PendingApproval') {
        pendingSumAcc += amount
        pendingCountAcc += 1
      }
    }

    return {
      approvedSum: approvedSumAcc,
      approvedCount: approvedCountAcc,
      pendingSum: pendingSumAcc,
      pendingCount: pendingCountAcc,
    }
  }, [variationOrders])

  return (
    <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
      <StatTile
        label="อนุมัติแล้ว"
        tone="success"
        value={`${approvedSum > 0 ? '+' : ''}${formatMoney(approvedSum)} บาท`}
        caption={`${approvedCount} รายการ`}
      />
      <StatTile
        label="รออนุมัติ"
        tone="warning"
        value={`${pendingSum > 0 ? '+' : ''}${formatMoney(pendingSum)} บาท`}
        caption={`${pendingCount} รายการ`}
      />
      <StatTile
        label="BAC ปัจจุบัน (รวม VO อนุมัติ)"
        state={bacLoadState === 'error' ? 'error' : bacLoadState === 'loading' ? 'loading' : 'ready'}
        value={currentBac === null ? undefined : `${formatMoney(currentBac)} บาท`}
        caption={currentBac === null && bacLoadState === 'error' ? 'ไม่สามารถโหลดข้อมูลโครงการได้ในขณะนี้' : undefined}
      />
    </div>
  )
}
