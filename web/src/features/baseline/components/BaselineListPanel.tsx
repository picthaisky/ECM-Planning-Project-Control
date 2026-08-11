import { Button, StatusPill } from '../../../components'
import { formatMoney } from '../../../utils/format'
import type { BaselineDto } from '../types'

export interface BaselineListPanelProps {
  baselines: BaselineDto[]
  loadState: 'loading' | 'ready'
  /** `false` means `GET .../baselines` 404'd (see `api.ts#listBaselines`'s remarks) — this list is
   * session-local only (baselines captured/activated *this* session), never a claim of
   * completeness. */
  listAvailable: boolean
  actionState: 'idle' | 'busy' | 'error'
  actionError: string | null
  canWrite: boolean
  onOpenCapture: () => void
  onActivate: (baselineId: string) => void
  /** Which baseline the comparison table currently targets — `null` means "the active baseline"
   * (the backend's own default). */
  selectedBaselineId: string | null
  onSelect: (baselineId: string | null) => void
}

const dateFormatter = new Intl.DateTimeFormat('th-TH', { dateStyle: 'medium', timeZone: 'Asia/Bangkok' })

function formatDate(iso: string): string {
  const parsed = new Date(iso)
  return Number.isNaN(parsed.getTime()) ? iso : dateFormatter.format(parsed)
}

/**
 * S14-FE-01 "จัดการหลายชุด Baseline + ตั้ง Active" — the prototype's Baseline list with the
 * activate/star action (`docs/ECM Planning Prototype.dc.html` Baseline screen).
 *
 * When `listAvailable` is `false` (today's real backend — `GET .../baselines` does not exist,
 * `api.ts#listBaselines`'s own remarks), this still functions: `capture`/`activate` are real, live
 * endpoints, so the list simply starts empty and grows from this session's own actions, with an
 * honest inline note rather than silently pretending it is the complete history.
 */
export function BaselineListPanel({
  baselines,
  loadState,
  listAvailable,
  actionState,
  actionError,
  canWrite,
  onOpenCapture,
  onActivate,
  selectedBaselineId,
  onSelect,
}: BaselineListPanelProps) {
  return (
    <div className="rounded-card border border-border bg-surface p-[18px]">
      <div className="mb-3 flex items-center justify-between">
        <div className="font-heading text-sm font-semibold text-navy">Baseline ทั้งหมด</div>
        {canWrite && (
          <Button size="sm" onClick={onOpenCapture} disabled={actionState === 'busy'}>
            + บันทึก Baseline ใหม่
          </Button>
        )}
      </div>

      {!listAvailable && (
        <p className="mb-3 rounded-card border border-warning-text/30 bg-warning-text/5 px-3 py-2 text-[10.5px] text-warning-text">
          ระบบยังไม่มี endpoint สำหรับดึงรายการ Baseline ทั้งหมด (ต้องการ <code>GET /api/v1/projects/{'{'}id{'}'}/baselines</code>)
          — รายการนี้จึงแสดงเฉพาะ Baseline ที่สร้าง/เปิดใช้งานในเซสชันนี้เท่านั้น ตารางเปรียบเทียบด้านล่างยังคงถูกต้องเสมอ
          (เทียบกับ Baseline ที่ Active อยู่จริงบนเซิร์ฟเวอร์)
        </p>
      )}

      {actionError && (
        <p role="alert" className="mb-3 text-[11px] text-danger">
          {actionError}
        </p>
      )}

      {loadState === 'loading' ? (
        <div role="status" className="py-8 text-center text-xs text-text-faint">
          กำลังโหลดรายการ Baseline...
        </div>
      ) : baselines.length === 0 ? (
        <div className="py-8 text-center text-xs text-text-faint">ยังไม่มี Baseline ที่สร้างในเซสชันนี้</div>
      ) : (
        <ul className="flex flex-col gap-2">
          {baselines.map((baseline) => {
            const isSelected = selectedBaselineId === baseline.id
            return (
              <li key={baseline.id}>
                <button
                  type="button"
                  onClick={() => onSelect(isSelected ? null : baseline.id)}
                  className={`w-full rounded-card border px-3 py-2.5 text-left transition-colors ${
                    isSelected ? 'border-gold bg-gold/10' : 'border-border bg-bg hover:border-navy/40'
                  }`}
                >
                  <div className="flex items-center justify-between gap-2">
                    <span className="truncate text-[12.5px] font-semibold text-navy">{baseline.name}</span>
                    {baseline.isActive && <StatusPill label="Active" tone="success" />}
                  </div>
                  <div className="mt-1 flex flex-wrap gap-x-3 gap-y-0.5 text-[10.5px] text-text-faint">
                    <span>บันทึกเมื่อ {formatDate(baseline.capturedAt)}</span>
                    <span>{baseline.activityCount.toLocaleString('th-TH')} กิจกรรม</span>
                    <span>BAC {formatMoney(baseline.bac)} บาท</span>
                  </div>
                </button>
                {canWrite && !baseline.isActive && (
                  <Button
                    size="sm"
                    variant="secondary"
                    className="mt-1.5"
                    loading={actionState === 'busy'}
                    onClick={() => onActivate(baseline.id)}
                  >
                    ตั้งเป็น Active
                  </Button>
                )}
              </li>
            )
          })}
        </ul>
      )}
    </div>
  )
}
