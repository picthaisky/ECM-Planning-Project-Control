import type { StatusPillTone } from './StatusPill'
import type { OutboxItemStatus } from '../services/outbox'

/**
 * Shared Thai labels/tones for `OutboxItemStatus` (`services/outbox/types.ts`) — one definition so
 * `PhotoOutboxList.tsx`, the weather/progress-batch offline queue lists (S13-FE-01), and
 * `SyncStatusBadge.tsx` (S13-FE-03) render the same word for the same state, and so the exhaustive
 * `Record<OutboxItemStatus, ...>` shape below forces a compile error — not a silent `undefined`
 * label — the moment a new status (e.g. `'conflict'`, S13-FE-01) is added without updating every
 * consumer.
 */
export const OUTBOX_STATUS_LABELS: Record<OutboxItemStatus, string> = {
  queued: 'รอซิงค์',
  syncing: 'กำลังซิงค์...',
  synced: 'ซิงค์แล้ว',
  failed: 'ซิงค์ไม่สำเร็จ',
  // S13-FE-01: terminal — `outboxStore.ts#markConflict`'s doc comment explains why this is
  // deliberately not worded as just another failure ("ไม่สำเร็จ") that will retry itself.
  conflict: 'ข้อมูลขัดแย้ง (ต้องตรวจสอบ)',
}

export const OUTBOX_STATUS_TONES: Record<OutboxItemStatus, StatusPillTone> = {
  queued: 'warning',
  syncing: 'warning',
  synced: 'success',
  failed: 'danger',
  conflict: 'danger',
}
