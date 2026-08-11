import { useEffect, useState } from 'react'
import { OUTBOX_STATUS_LABELS, OUTBOX_STATUS_TONES, StatusPill } from '../../../components'
import type { OutboxItem } from '../../../services/outbox'
import type { PhotoOutboxPayload } from '../photoOutbox'

export interface PhotoOutboxListProps {
  items: OutboxItem<PhotoOutboxPayload>[]
  onRetry: () => void
}

const DATE_TIME_FORMATTER = new Intl.DateTimeFormat('th-TH', {
  dateStyle: 'medium',
  timeStyle: 'short',
  timeZone: 'Asia/Bangkok',
})

function formatCreatedAt(iso: string): string {
  const parsed = new Date(iso)
  return Number.isNaN(parsed.getTime()) ? iso : DATE_TIME_FORMATTER.format(parsed)
}

/** Renders a live thumbnail from the outbox item's own locally-stored blob (queued/syncing/failed
 * items still carry it) via an object URL, revoked on unmount/blob change so this list never leaks
 * memory as items cycle through the queue. A `synced` item has its blob dropped deliberately
 * (`outboxStore.ts#markSynced`) to keep IndexedDB usage bounded, so it falls back to a plain
 * placeholder instead of a thumbnail — the photo itself is safely on the server by then. */
function PhotoThumbnail({ blob }: { blob: Blob | null }) {
  const [objectUrl, setObjectUrl] = useState<string | null>(null)

  useEffect(() => {
    if (!blob) {
      setObjectUrl(null)
      return undefined
    }
    const url = URL.createObjectURL(blob)
    setObjectUrl(url)
    return () => URL.revokeObjectURL(url)
  }, [blob])

  if (!objectUrl) {
    return (
      <div className="flex aspect-[4/3] items-center justify-center rounded bg-bg text-[10px] text-text-faint">
        ซิงค์แล้ว — ไม่เก็บสำเนาในเครื่อง
      </div>
    )
  }

  return <img src={objectUrl} alt="ตัวอย่างรูปภาพที่รอซิงค์" className="aspect-[4/3] w-full rounded object-cover" />
}

/**
 * S12-FE-01's outbox status list — the DoD's "ตัดเน็ตแล้วถ่ายรูป → เข้า IndexedDB outbox แสดง 'รอซิงค์';
 * เน็ตกลับมา → ... ขึ้น 'ซิงค์แล้ว'" made visible. Card-grid layout echoes the prototype's Photo
 * Progress grid (`docs/ECM Planning Prototype.dc.html` ~line 367) rather than a dense data table —
 * a device's own outbox realistically holds a handful to a few dozen items at a time, well below
 * `DataTable`'s virtualization threshold (ADR-0004 targets 10,000+ row Gantt/WBS-scale lists).
 */
export function PhotoOutboxList({ items, onRetry }: PhotoOutboxListProps) {
  if (items.length === 0) {
    return (
      <div className="flex h-40 items-center justify-center rounded-card border border-dashed border-border text-xs text-text-faint">
        ยังไม่มีรูปภาพในคิวของอุปกรณ์นี้
      </div>
    )
  }

  return (
    <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-4">
      {items.map((item) => (
        <div
          key={item.id}
          data-testid="photo-outbox-item"
          data-outbox-status={item.status}
          className="overflow-hidden rounded-card border border-border bg-surface"
        >
          <PhotoThumbnail blob={item.blob} />
          <div className="space-y-1 p-2.5">
            <div className="flex items-center justify-between gap-2">
              <StatusPill label={OUTBOX_STATUS_LABELS[item.status]} tone={OUTBOX_STATUS_TONES[item.status]} />
              <span className="text-[10px] text-text-faint">{formatCreatedAt(item.createdAt)}</span>
            </div>
            {item.payload.fields.caption && (
              <p className="truncate text-[11px] font-medium text-text" title={item.payload.fields.caption}>
                {item.payload.fields.caption}
              </p>
            )}
            {item.payload.fields.activityId && (
              <p className="truncate font-mono text-[10px] text-text-faint" title={item.payload.fields.activityId}>
                ผูกกับ {item.payload.fields.activityId}
              </p>
            )}
            {item.status === 'failed' && (
              <div className="space-y-1">
                <p className="text-[10.5px] text-danger">{item.lastError ?? 'อัปโหลดไม่สำเร็จ'}</p>
                <button
                  type="button"
                  onClick={onRetry}
                  className="text-[10.5px] font-medium text-navy underline decoration-dotted hover:text-navy/70"
                >
                  ลองซิงค์ใหม่
                </button>
              </div>
            )}
            {/* S13-FE-01: a `conflict` item's payload has already been rejected once by an
                Idempotency-Key match on the server — offering "ลองซิงค์ใหม่" here would resend the
                exact same payload into the exact same rejection, so unlike `failed` there is
                deliberately no retry action; the message says so plainly rather than leaving the user
                to wonder why this one has no button. */}
            {item.status === 'conflict' && (
              <p className="text-[10.5px] text-danger">
                {item.lastError ?? 'ข้อมูลขัดแย้งกับรายการที่เคยส่งไปแล้ว'} — ระบบจะไม่ลองส่งซ้ำอัตโนมัติ
              </p>
            )}
          </div>
        </div>
      ))}
    </div>
  )
}
