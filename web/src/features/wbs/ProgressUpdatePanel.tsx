import { useId, useMemo, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import { observeElementRect, useVirtualizer } from '@tanstack/react-virtual'
import { Button, OUTBOX_STATUS_LABELS, OUTBOX_STATUS_TONES, StatusPill } from '../../components'
import { formatPercent } from '../../utils/format'
import { DecreaseConfirmModal } from './DecreaseConfirmModal'
import type { useBatchProgressForm } from './useBatchProgressForm'

export interface ProgressUpdatePanelProps {
  form: ReturnType<typeof useBatchProgressForm>
  /** Set while `WbsTreeGrid`'s "+ เพิ่มกิจกรรม" action is loading a node's activities
   * (`useNodeActivities`) — surfaced here since this panel is where the resulting rows land. */
  nodeActivitiesLoading?: boolean
  nodeActivitiesError?: string | null
}

const inputClass =
  'rounded-card border border-border px-2.5 py-1.5 text-xs text-text focus:border-navy focus:outline-none'

/** Row height (px) — sized for the two-line activity cell (code + optional name subtitle). */
const ROW_HEIGHT = 56
/** Scrollable viewport height (px), and the measurement floor below (ADR-0004). */
const VIEWPORT_HEIGHT = 480

/**
 * Wraps the library's own `observeElementRect` (same one `DataTable`/`WbsTreeGrid` get by default
 * from `useVirtualizer`) and floors the measured height at `VIEWPORT_HEIGHT`. jsdom performs no
 * real layout, so a scroll container's `offsetHeight` is `0` unless a test explicitly stubs it
 * (`DataTable.test.tsx` does this file-wide for exactly this reason) — most of this component's
 * own tests deliberately don't, since they exercise ordinary small-scale editing rather than
 * virtualization at scale, and should keep seeing their rows regardless. Real browsers already
 * report >= `VIEWPORT_HEIGHT` here (the scroll container's CSS height below), so the floor is a
 * no-op in production and in the dedicated large-row-count test, which stubs a taller viewport.
 */
const observeElementRectWithMinHeight: typeof observeElementRect = (instance, cb) =>
  observeElementRect(instance, (rect) => cb({ width: rect.width, height: Math.max(rect.height, VIEWPORT_HEIGHT) }))

/**
 * S4-FE-03 "โหมดอัปเดตความคืบหน้า" batch grid (US-4.5): one shared `PeriodEndDate`, N editable
 * rows (each calling `Activity.RecordProgress` server-side via the real
 * `POST .../progress/batch`), the decrease-confirmation gate, and a manual "+ เพิ่มกิจกรรมด้วยรหัส"
 * add-row path since there is no live endpoint yet to list a node's activities
 * (`api.ts`'s `getNodeActivities` remarks) — rows populated that way carry no known "current %",
 * so decrease-detection simply does not apply to them (nothing to compare against).
 *
 * The row grid renders on `@tanstack/react-virtual` directly — the same library `DataTable`
 * (Sprint 1) wraps for the read-only WBS tree grid, applied here the same way internally (a
 * `useVirtualizer` instance sized off a scrollable viewport, rows absolutely positioned via
 * `transform: translateY`) — because every row here carries live per-row input state, not the
 * static cell values `DataTable`'s generic `render(row)` column contract was built for (ADR-0004:
 * ≥500-row lists must never mount one DOM node per row).
 */
export function ProgressUpdatePanel({ form, nodeActivitiesLoading, nodeActivitiesError }: ProgressUpdatePanelProps) {
  const [manualId, setManualId] = useState('')
  const [manualError, setManualError] = useState<string | null>(null)
  const periodInputId = useId()
  const scrollRef = useRef<HTMLDivElement>(null)

  function handleAddManual(event: FormEvent) {
    event.preventDefault()
    const error = form.addManualRow(manualId)
    setManualError(error)
    if (!error) setManualId('')
  }

  const decreasedIds = useMemo(
    () => new Set(form.decreasedRows.map((r) => r.activityId)),
    [form.decreasedRows],
  )

  const rowVirtualizer = useVirtualizer({
    count: form.rows.length,
    getScrollElement: () => scrollRef.current,
    estimateSize: () => ROW_HEIGHT,
    overscan: 8,
    observeElementRect: observeElementRectWithMinHeight,
  })

  return (
    <div className="rounded-card border border-border bg-surface p-4">
      <div className="flex flex-wrap items-center gap-3">
        <div>
          <label htmlFor={periodInputId} className="block text-[11px] text-text-faint">
            วันที่ของงวดข้อมูล (Period End Date)
          </label>
          <input
            id={periodInputId}
            type="date"
            value={form.periodEndDate}
            onChange={(e) => form.setPeriodEndDate(e.target.value)}
            className={`mt-1 ${inputClass}`}
          />
        </div>

        <form onSubmit={handleAddManual} className="ml-auto flex items-end gap-2">
          <div>
            <label htmlFor="manual-activity-id" className="block text-[11px] text-text-faint">
              เพิ่มกิจกรรมด้วยรหัส (Activity ID)
            </label>
            <input
              id="manual-activity-id"
              type="text"
              placeholder="3fa85f64-5717-4562-b3fc-2c963f66afa6"
              value={manualId}
              onChange={(e) => setManualId(e.target.value)}
              className={`mt-1 w-64 font-mono ${inputClass}`}
            />
          </div>
          <Button type="submit" size="sm" variant="secondary">
            + เพิ่ม
          </Button>
        </form>
      </div>
      {manualError && (
        <p role="alert" className="mt-1.5 text-[10.5px] text-danger">
          {manualError}
        </p>
      )}
      {nodeActivitiesLoading && (
        <p className="mt-1.5 text-[10.5px] text-text-faint">กำลังโหลดกิจกรรมจากหมวดที่เลือก...</p>
      )}
      {nodeActivitiesError && (
        <p className="mt-1.5 text-[10.5px] text-warning-text">
          {nodeActivitiesError} — ยังสามารถเพิ่มกิจกรรมด้วยรหัส (Activity ID) ได้ตามช่องด้านบน
        </p>
      )}

      <div className="mt-4 overflow-hidden rounded-card border border-border" role="table" aria-label="ตารางปรับปรุงความคืบหน้า">
        <div
          role="row"
          className="grid grid-cols-[1fr_100px_110px_100px_60px] bg-surface-muted text-[10.5px] font-semibold uppercase tracking-wide text-text-faint"
        >
          <div className="px-3 py-2">กิจกรรม</div>
          <div className="px-3 py-2 text-right">ปัจจุบัน %</div>
          <div className="px-3 py-2 text-right">% ใหม่</div>
          <div className="px-3 py-2 text-right">ปริมาณจริง</div>
          <div className="px-3 py-2" />
        </div>

        {form.rows.length === 0 ? (
          <div className="px-3 py-8 text-center text-xs text-text-faint">
            ยังไม่มีกิจกรรมในชุดอัปเดตนี้ — เลือก &quot;+ เพิ่มกิจกรรม&quot; จากตาราง WBS ด้านบน หรือเพิ่มด้วยรหัส
          </div>
        ) : (
          // ADR-0004: only the visible window + overscan is mounted, regardless of `form.rows.length`
          // (verified at 600 rows in `ProgressUpdatePanel.test.tsx`).
          <div ref={scrollRef} className="overflow-auto" style={{ height: VIEWPORT_HEIGHT }}>
            <div style={{ height: rowVirtualizer.getTotalSize(), position: 'relative' }}>
              {rowVirtualizer.getVirtualItems().map((virtualRow) => {
                const row = form.rows[virtualRow.index]
                const decreased = decreasedIds.has(row.activityId)
                return (
                  <div
                    key={row.activityId}
                    role="row"
                    data-index={virtualRow.index}
                    className="grid grid-cols-[1fr_100px_110px_100px_60px] border-t border-border-subtle text-xs"
                    style={{
                      position: 'absolute',
                      top: 0,
                      left: 0,
                      width: '100%',
                      height: virtualRow.size,
                      transform: `translateY(${virtualRow.start}px)`,
                    }}
                  >
                    <div className="truncate px-3 py-2">
                      <div className="font-medium text-text">{row.activityCode ?? row.activityId}</div>
                      {row.name && <div className="truncate text-[10.5px] text-text-faint">{row.name}</div>}
                    </div>
                    <div className="px-3 py-2 text-right text-text-muted">
                      {row.currentProgressPercentage === null ? '—' : formatPercent(row.currentProgressPercentage)}
                    </div>
                    <div className="px-3 py-2 text-right">
                      <input
                        type="number"
                        step="0.01"
                        min="0"
                        max="100"
                        aria-label={`% ความคืบหน้าใหม่ของ ${row.activityCode ?? row.activityId}`}
                        value={row.newProgressPercentage}
                        onChange={(e) => form.updateRowProgress(row.activityId, e.target.value)}
                        className={`w-full text-right ${inputClass} ${decreased ? 'border-danger text-danger' : ''}`}
                      />
                    </div>
                    <div className="px-3 py-2 text-right">
                      <input
                        type="number"
                        step="0.01"
                        aria-label={`ปริมาณงานจริงของ ${row.activityCode ?? row.activityId}`}
                        value={row.actualQuantity}
                        onChange={(e) => form.updateRowQuantity(row.activityId, e.target.value)}
                        className={`w-full text-right ${inputClass}`}
                      />
                    </div>
                    <div className="flex items-center justify-end px-3 py-2">
                      <button
                        type="button"
                        aria-label={`ลบกิจกรรม ${row.activityCode ?? row.activityId}`}
                        onClick={() => form.removeRow(row.activityId)}
                        className="text-danger hover:text-danger/70"
                      >
                        &times;
                      </button>
                    </div>
                  </div>
                )
              })}
            </div>
          </div>
        )}
      </div>

      {form.validationError && (
        <p role="alert" className="mt-3 text-xs text-danger">
          {form.validationError}
        </p>
      )}
      {form.submitError && (
        <p role="alert" className="mt-3 text-xs text-danger">
          {form.submitError}
        </p>
      )}
      {/* S13-FE-01 (ADR-0005): submission always enqueues first — `form.lastResultCount` is now "rows
          queued this submit", not a server confirmation, so this reads "คิวไว้แล้ว" rather than
          "สำเร็จ"; the queue list below shows each batch's *real* sync outcome. */}
      {form.lastResultCount !== null && form.submitState === 'idle' && form.rows.length === 0 && (
        <p role="status" className="mt-3 text-xs text-success">
          คิวไว้แล้ว {form.lastResultCount.toLocaleString('th-TH')} รายการ (รอซิงค์)
        </p>
      )}

      <div className="mt-4 flex justify-end">
        <Button
          size="sm"
          loading={form.submitState === 'submitting'}
          onClick={() => void form.attemptSubmit()}
        >
          ส่งข้อมูลความคืบหน้า
        </Button>
      </div>

      <DecreaseConfirmModal
        isOpen={form.pendingConfirmation}
        rows={form.decreasedRows}
        onCancel={form.cancelDecreaseConfirmation}
        onConfirm={() => void form.confirmDecreaseAndSubmit()}
        confirming={form.submitState === 'submitting'}
      />

      <ProgressOutboxQueue form={form} />
    </div>
  )
}

/**
 * S13-FE-01's "เข้าคิวพร้อมสถานะรายรายการ" DoD for batch progress — this device's own not-yet-synced
 * batch queue for this project, mirroring `features/photo/components/PhotoOutboxList.tsx`'s status-
 * pill discipline. Each item is one whole batch submit (`useProgressBatchOutbox.ts`'s one-enqueue-
 * per-submit shape), so the row count shown is entries-per-batch, not per-activity.
 *
 * The header (title + "ซิงค์เดี๋ยวนี้") always renders, regardless of `pending.length` — mirroring
 * `WeatherPage.tsx`'s identical layout choice, and deliberately *not* the shape this component had
 * before: the whole block used to return `null` once nothing was pending, which meant the button
 * itself unmounted the instant the last item finished syncing — a real, observed failure mode (a
 * click landing at exactly that moment loses its target mid-action), not merely a cosmetic one.
 */
function ProgressOutboxQueue({ form }: { form: ReturnType<typeof useBatchProgressForm> }) {
  const pending = form.outboxItems.filter((item) => item.status !== 'synced')

  return (
    <div className="mt-4 border-t border-border-subtle pt-3">
      <div className="flex items-center justify-between">
        <div className="font-heading text-[12px] font-semibold text-navy">คิวออฟไลน์ของอุปกรณ์นี้ (โครงการนี้)</div>
        <Button size="sm" variant="secondary" onClick={() => void form.syncNow()}>
          ซิงค์เดี๋ยวนี้
        </Button>
      </div>
      {form.syncCapability === 'fallback-only' && (
        <p className="mt-1 text-[10.5px] text-text-faint">
          อุปกรณ์นี้ไม่รองรับการซิงค์อัตโนมัติเบื้องหลัง (เช่น iOS/Safari) — ระบบจะซิงค์ให้ทันทีเมื่อเชื่อมต่ออินเทอร์เน็ตขณะเปิดหน้านี้ค้างไว้
          หรือกดปุ่ม &quot;ซิงค์เดี๋ยวนี้&quot;
        </p>
      )}
      {pending.length === 0 ? (
        <p className="mt-2 text-[10.5px] text-text-faint">ไม่มีรายการค้างซิงค์ในเครื่องนี้สำหรับโครงการนี้</p>
      ) : (
        <div className="mt-2 space-y-2">
          {pending.map((item) => (
            <div
              key={item.id}
              data-testid="progress-outbox-item"
              data-outbox-status={item.status}
              className="flex flex-wrap items-center gap-2 rounded-card border border-border bg-surface px-3 py-2"
            >
              <StatusPill label={OUTBOX_STATUS_LABELS[item.status]} tone={OUTBOX_STATUS_TONES[item.status]} />
              <span className="text-[11px] text-text-faint">{item.payload.request.entries.length} รายการ</span>
              {(item.status === 'failed' || item.status === 'conflict') && item.lastError && (
                <span className="w-full text-[10.5px] text-danger sm:w-auto sm:flex-1">{item.lastError}</span>
              )}
            </div>
          ))}
        </div>
      )}
    </div>
  )
}
