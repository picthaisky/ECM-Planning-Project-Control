import { StatTile } from '../../../components'
import type { IssueStatusCountsDto } from '../types'

export interface IssueSummaryTilesProps {
  /** Deliberately **not** `items: IssueLogDto[]` — only the server's own aggregate. This component
   * has no way to see the row list at all, which makes it structurally impossible for these tiles to
   * be computed from (and therefore ever drift from) the table's rows — the exact DoD requirement
   * ("tile counts must match the table... use the backend's `statusCounts` rather than counting
   * client-side, so the tile and the table cannot disagree"). */
  totalCount: number
  statusCounts: IssueStatusCountsDto
  state: 'loading' | 'ready' | 'error'
  errorMessage?: string
}

/**
 * The Issue / Action Log screen's four summary tiles (S11-FE-01, matching the prototype's own
 * `docs/ECM Planning Prototype.dc.html` ~line 497-500 set and colors exactly: ทั้งหมด/navy,
 * เปิดอยู่/danger, กำลังแก้ไข/warning, ปิดแล้ว/success).
 */
export function IssueSummaryTiles({ totalCount, statusCounts, state, errorMessage }: IssueSummaryTilesProps) {
  return (
    <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
      <StatTile label="ทั้งหมด" state={state} errorMessage={errorMessage} value={totalCount.toLocaleString('th-TH')} />
      <StatTile
        label="เปิดอยู่ (Open)"
        state={state}
        errorMessage={errorMessage}
        tone="danger"
        value={statusCounts.open.toLocaleString('th-TH')}
      />
      <StatTile
        label="กำลังแก้ไข (Doing)"
        state={state}
        errorMessage={errorMessage}
        tone="warning"
        value={statusCounts.doing.toLocaleString('th-TH')}
      />
      <StatTile
        label="ปิดแล้ว (Closed)"
        state={state}
        errorMessage={errorMessage}
        tone="success"
        value={statusCounts.closed.toLocaleString('th-TH')}
      />
    </div>
  )
}
