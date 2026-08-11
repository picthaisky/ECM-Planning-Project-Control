import { useParams } from 'react-router-dom'
import { Button } from '../../components'
import { PhotoCaptureForm } from './components/PhotoCaptureForm'
import { PhotoOutboxList } from './components/PhotoOutboxList'
import { usePhotoOutbox } from './usePhotoOutbox'

/**
 * S12-FE-01 "Photo Progress" screen (US-12.1) — offline capture + the first outbox (ADR-0005).
 * There is no project-wide photo gallery here: the Sprint 12 backend exposes no `GET` list endpoint
 * for photos (only `POST` upload and `GET .../photos/{photoId}` for one photo's bytes — confirmed
 * directly against `ProjectPhotosController.cs`), and the DoD itself only asks for capture -> outbox
 * -> sync status, not browsing already-uploaded photos — so this screen shows this device's own
 * outbox, which is real, live data (not a fabricated gallery).
 *
 * Sprint 12 security review, H-02/L-06: on a shared device, "this device's own outbox" is *not* the
 * same as "everything queued on this device" — `usePhotoOutbox.ts` scopes the list to the current
 * signed-in owner (never another user's items, even mid-session on the same device) and to this
 * route's `projectId` (never another project's queued items bleeding into this screen).
 */
export function PhotoPage() {
  const { projectId } = useParams<{ projectId: string }>()
  const outbox = usePhotoOutbox(projectId ?? '')

  if (!projectId) return null

  const pendingCount = outbox.items.filter((item) => item.status === 'queued' || item.status === 'failed').length

  return (
    <div className="flex flex-col gap-4">
      <div
        role="status"
        className="rounded-card border border-border bg-surface px-4 py-3 text-[11.5px] text-text-muted"
      >
        {outbox.syncCapability === 'background-sync' ? (
          <span>
            อุปกรณ์นี้รองรับการซิงค์อัตโนมัติเบื้องหลัง (Background Sync) — เมื่อเชื่อมต่ออินเทอร์เน็ตอีกครั้ง
            ระบบจะพยายามซิงค์รูปที่ค้างอยู่ให้อัตโนมัติ
          </span>
        ) : (
          <span>
            อุปกรณ์นี้ไม่รองรับการซิงค์อัตโนมัติเบื้องหลัง (เช่น iOS/Safari) — ระบบจะซิงค์ให้ทันทีเมื่อเชื่อมต่ออินเทอร์เน็ต
            <span className="font-semibold"> ขณะเปิดหน้านี้ค้างไว้</span> หรือเมื่อกลับมาเปิดแอปอีกครั้งขณะออนไลน์ —
            กรุณาอย่าปิดแอปจนกว่ารูปจะขึ้นสถานะ &quot;ซิงค์แล้ว&quot;
          </span>
        )}
        {pendingCount > 0 && (
          <span className="ml-2 font-medium text-warning-text">รอซิงค์อยู่ {pendingCount} รายการ</span>
        )}
      </div>

      <PhotoCaptureForm onCapture={outbox.capture} busy={outbox.processingState === 'compressing'} errorMessage={outbox.processingError} />

      <div className="flex items-center justify-between">
        {/* L-06 / H-02 (Sprint 12 security review): the list itself is now scoped to *this* signed-in
            user and *this* project (`usePhotoOutbox.ts#reload`) — the heading says so, rather than
            the previous "this device's queue", which overstated what a shared tablet's screen
            actually shows once ownership scoping is in place. */}
        <h3 className="font-heading text-sm font-semibold text-navy">คิวรูปภาพของฉันสำหรับโครงการนี้</h3>
        <Button size="sm" variant="secondary" onClick={() => void outbox.syncNow()}>
          ซิงค์เดี๋ยวนี้
        </Button>
      </div>

      <PhotoOutboxList items={outbox.items} onRetry={() => void outbox.syncNow()} />
    </div>
  )
}
