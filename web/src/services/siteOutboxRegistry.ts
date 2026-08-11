import { PHOTO_OUTBOX_KIND, uploadPhotoOutboxItem } from '../features/photo/photoOutbox'
import {
  WEATHER_LOG_CORRECTION_OUTBOX_KIND,
  WEATHER_LOG_OUTBOX_KIND,
  createWeatherLogCorrectionUploader,
  uploadWeatherLogOutboxItem,
} from '../features/weather/weatherOutbox'
import { PROGRESS_BATCH_OUTBOX_KIND, uploadProgressBatchOutboxItem } from '../features/wbs/progressOutbox'
import type { OutboxStore, OutboxUploader } from './outbox'

/**
 * S13-FE-03's composition root: the one place in the app that knows every outbox `kind` that exists,
 * so the system-wide sync status UI (`SyncStatusBadge.tsx`, via `useOutboxSyncStatus.ts`) can show
 * accurate totals and offer a real "sync everything now" action from *any* screen — not only the one
 * feature page (Photo/Weather/WBS) that happens to be mounted. Deliberately lives outside
 * `services/outbox/` (never inside it): that module's own files repeatedly document that they must
 * stay kind-agnostic and never import feature code — this file is the one exception point, at the
 * composition layer, the same architectural position `routes/AppRoutes.tsx` already occupies for
 * screens.
 *
 * Each feature hook (`usePhotoOutbox`, `useWeatherLogOutbox`, `useProgressBatchOutbox`) *also*
 * registers its own uploader(s) independently, in its own `createSyncEngine` call — this registry is
 * not a replacement for those, it is a second, independent registration for the badge's own engine.
 * That is deliberate, not an oversight: before S13-BE-01, two engines racing to flush the same item
 * could have double-uploaded it; now that every wrapped endpoint honours `Idempotency-Key`
 * (`services/outbox/errors.ts`), a second concurrent attempt on an item another engine is already
 * mid-upload on resolves to a harmless, already-handled `IdempotencyRequestInProgress` retry rather
 * than a duplicate record — so accepting the (rare) redundant attempt is a smaller cost than giving
 * every screen in the app a hard dependency on one single global engine instance.
 */
export const ALL_OUTBOX_KINDS: readonly string[] = [
  PHOTO_OUTBOX_KIND,
  WEATHER_LOG_OUTBOX_KIND,
  WEATHER_LOG_CORRECTION_OUTBOX_KIND,
  PROGRESS_BATCH_OUTBOX_KIND,
]

/** `store` is threaded through because `WEATHER_LOG_CORRECTION_OUTBOX_KIND`'s uploader needs it (a
 * correction's own upload behaviour depends on another outbox item's state — see
 * `features/weather/weatherOutbox.ts`) — every other kind's uploader ignores it. */
export function buildSiteOutboxUploaders(store: OutboxStore): Record<string, OutboxUploader> {
  return {
    [PHOTO_OUTBOX_KIND]: uploadPhotoOutboxItem as OutboxUploader,
    [WEATHER_LOG_OUTBOX_KIND]: uploadWeatherLogOutboxItem as OutboxUploader,
    [WEATHER_LOG_CORRECTION_OUTBOX_KIND]: createWeatherLogCorrectionUploader(store) as OutboxUploader,
    [PROGRESS_BATCH_OUTBOX_KIND]: uploadProgressBatchOutboxItem as OutboxUploader,
  }
}

/** Thai display label per `kind`, for the badge's per-item list — one shared table so
 * `SyncStatusBadge.tsx` never has to duplicate what each feature already calls its own kind. */
export const OUTBOX_KIND_LABELS: Record<string, string> = {
  [PHOTO_OUTBOX_KIND]: 'รูปภาพ',
  [WEATHER_LOG_OUTBOX_KIND]: 'บันทึกสภาพอากาศ',
  [WEATHER_LOG_CORRECTION_OUTBOX_KIND]: 'แก้ไขบันทึกสภาพอากาศ',
  [PROGRESS_BATCH_OUTBOX_KIND]: 'ความคืบหน้า (batch)',
}

export function outboxKindLabel(kind: string): string {
  return OUTBOX_KIND_LABELS[kind] ?? kind
}
