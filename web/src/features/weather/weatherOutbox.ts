import { recordWeatherLog, recordWeatherLogCorrection, WeatherApiError } from './api'
import { IDEMPOTENCY_CONFLICT_CODES, OutboxConflictError } from '../../services/outbox'
import type { OutboxItem, OutboxStore, OutboxUploader } from '../../services/outbox'
import type { RecordWeatherLogCorrectionPayload, RecordWeatherLogPayload, WeatherLogDto } from './types'

/**
 * S13-FE-01: extends the generic outbox (`services/outbox/`, ADR-0005) to Weather Log — the second
 * `kind` after `'photo'` (S12-FE-01), and the exact one the Sprint 12 security review's H-02
 * re-verification proved the ownership seam against (`docs/security/reviews/sprint-12.md` §9.3, §10.1
 * closing line). Two kinds, not one, because an Original entry and a Correction/Retraction are
 * genuinely different write shapes (a correction needs a *target*) with a genuine ordering
 * dependency between them — see `WeatherLogTargetRef`/`createWeatherLogCorrectionUploader` below.
 */

export const WEATHER_LOG_OUTBOX_KIND = 'weather-log'
export const WEATHER_LOG_CORRECTION_OUTBOX_KIND = 'weather-log-correction'

export interface WeatherLogOutboxPayload {
  projectId: string
  fields: RecordWeatherLogPayload
}

/**
 * What a queued correction targets. A correction's URL (`POST .../weather-logs/{logId}/corrections`)
 * needs the *server* id of the row it corrects — but the row being corrected might itself still be
 * sitting unsynced in this same device's outbox (the site engineer typed today's log offline, then
 * immediately noticed a typo and queued a correction for it before either one ever reached the
 * server). `{ type: 'local' }` defers resolving the real server id until sync time
 * (`resolveWeatherLogTargetId` below) instead of blocking the user from queuing the correction at all.
 */
export type WeatherLogTargetRef = { type: 'server'; logId: string } | { type: 'local'; outboxItemId: string }

export interface WeatherLogCorrectionOutboxPayload {
  projectId: string
  target: WeatherLogTargetRef
  fields: RecordWeatherLogCorrectionPayload
}

/** Prefix marking a `WeatherLogDto.id`-shaped string as a *synthetic* local reference rather than a
 * real server GUID — used only client-side (`pendingWeatherLogItemToDto`/`parseWeatherLogTargetRef`)
 * so `WeatherCorrectionModal` (built for a real `WeatherLogDto`) can be reused unchanged to correct a
 * not-yet-synced local entry. Never a valid `Guid` string, so it can never collide with a real id and
 * is never sent to the server as-is (`recordWeatherLogCorrection`'s `logId` parameter is always the
 * *resolved* id, from `resolveWeatherLogTargetId`, not this raw string). */
const LOCAL_TARGET_PREFIX = 'local-pending:'

export function toLocalWeatherLogTargetId(outboxItemId: string): string {
  return `${LOCAL_TARGET_PREFIX}${outboxItemId}`
}

/** Inverse of `toLocalWeatherLogTargetId` — turns whatever id `WeatherCorrectionModal.onSubmit` hands
 * back (a real server GUID, or a synthetic local-target id) into a `WeatherLogTargetRef`. */
export function parseWeatherLogTargetRef(id: string): WeatherLogTargetRef {
  return id.startsWith(LOCAL_TARGET_PREFIX)
    ? { type: 'local', outboxItemId: id.slice(LOCAL_TARGET_PREFIX.length) }
    : { type: 'server', logId: id }
}

/** Wraps a raw API failure into `OutboxConflictError` for the idempotency codes that mean "blind
 * retry will never succeed" (S13-FE-01) — shared by both uploaders below. */
function toWeatherOutboxError(error: unknown): unknown {
  if (error instanceof WeatherApiError && error.status === 409 && error.code && IDEMPOTENCY_CONFLICT_CODES.has(error.code)) {
    return new OutboxConflictError(error.message)
  }
  return error
}

/** `syncEngine.ts`'s uploader for `kind: WEATHER_LOG_OUTBOX_KIND` — an Original entry, no target
 * dependency, so this mirrors `photoOutbox.ts#uploadPhotoOutboxItem` closely. */
export const uploadWeatherLogOutboxItem: OutboxUploader<WeatherLogOutboxPayload> = async (item) => {
  try {
    const saved = await recordWeatherLog(item.payload.projectId, item.payload.fields, item.idempotencyKey)
    return { serverId: saved.id }
  } catch (error) {
    throw toWeatherOutboxError(error)
  }
}

export type WeatherLogTargetResolution =
  | { status: 'ready'; logId: string }
  /** The target is a local item that has not synced (or is itself mid-sync) yet — a genuine ordering
   * dependency, not a failure of *this* correction. */
  | { status: 'pending' }
  /** The target local item no longer exists in this owner's outbox at all (e.g. the 7-day synced-item
   * retention sweep reaped it long after it synced — `services/outbox/outboxMaintenance.ts`). Distinct
   * from `'pending'` so the user is told the truth ("go re-check the table") instead of being told to
   * keep waiting for something that will never arrive. */
  | { status: 'missing' }

/**
 * Resolves a `WeatherLogTargetRef` to the real server id a correction's URL needs, reading the *live*
 * state of the local outbox (never a snapshot captured earlier in the same `flush()` pass) — so if the
 * target Original was enqueued before this correction (the only order the UI allows,
 * see `useWeatherLogOutbox.ts#correctableLocalOriginals`) and already synced earlier in *this very*
 * flush pass (items are processed oldest-first, `outboxStore.ts#list`), this resolves immediately
 * rather than waiting for a whole separate flush cycle.
 */
export async function resolveWeatherLogTargetId(
  store: OutboxStore,
  target: WeatherLogTargetRef,
): Promise<WeatherLogTargetResolution> {
  if (target.type === 'server') return { status: 'ready', logId: target.logId }

  const localItems = await store.list(WEATHER_LOG_OUTBOX_KIND)
  const original = localItems.find((candidate) => candidate.id === target.outboxItemId)
  if (!original) return { status: 'missing' }
  if (original.status === 'synced' && original.serverId) return { status: 'ready', logId: original.serverId }
  return { status: 'pending' }
}

/** S13-FE-01 task brief: "a queued correction referencing a log that hasn't synced yet is a genuine
 * ordering problem" — this is that message. Deliberately reassuring rather than alarming (it is not a
 * failure of the correction itself), and explicit that no user action is needed: the very next flush
 * that finds the original synced resolves this automatically, including — the common case — later in
 * this *same* flush pass. Recorded via the ordinary `markFailed` path (not `OutboxConflictError`), so
 * it stays in `PENDING_STATUSES` and is retried by every subsequent flush, exactly as this wait
 * requires. */
export const WEATHER_LOG_CORRECTION_WAITING_MESSAGE =
  'รอซิงค์บันทึกสภาพอากาศต้นฉบับให้เสร็จก่อน — ระบบจะส่งรายการแก้ไขนี้ให้อัตโนมัติทันทีที่บันทึกต้นฉบับซิงค์สำเร็จ ไม่ต้องดำเนินการเพิ่มเติม'

/** The target local item is gone from this device (see `WeatherLogTargetResolution`'s `'missing'`
 * doc comment) — a real, if rare, dead end this correction cannot recover from by retrying, so it is
 * worded as an instruction rather than a promise of automatic resolution. */
export const WEATHER_LOG_CORRECTION_TARGET_MISSING_MESSAGE =
  'ไม่พบบันทึกต้นฉบับของรายการแก้ไขนี้ในเครื่องนี้แล้ว กรุณาโหลดหน้าใหม่แล้วลองแก้ไขจากรายการในตารางแทน'

/**
 * `syncEngine.ts`'s uploader for `kind: WEATHER_LOG_CORRECTION_OUTBOX_KIND`. Takes `store` (the same
 * instance the owning hook constructed) as an explicit dependency — unlike every other uploader in
 * this codebase, a correction's own upload behaviour depends on the state of *another* outbox item,
 * so it cannot be a bare top-level function the way `uploadWeatherLogOutboxItem`/
 * `uploadPhotoOutboxItem` are.
 */
export function createWeatherLogCorrectionUploader(store: OutboxStore): OutboxUploader<WeatherLogCorrectionOutboxPayload> {
  return async (item) => {
    const resolution = await resolveWeatherLogTargetId(store, item.payload.target)

    if (resolution.status === 'pending') {
      throw new Error(WEATHER_LOG_CORRECTION_WAITING_MESSAGE)
    }
    if (resolution.status === 'missing') {
      throw new Error(WEATHER_LOG_CORRECTION_TARGET_MISSING_MESSAGE)
    }

    try {
      const saved = await recordWeatherLogCorrection(
        item.payload.projectId,
        resolution.logId,
        item.payload.fields,
        item.idempotencyKey,
      )
      return { serverId: saved.id }
    } catch (error) {
      throw toWeatherOutboxError(error)
    }
  }
}

/**
 * Synthesizes a `WeatherLogDto`-shaped view of a not-yet-synced local Original item, purely so
 * `WeatherCorrectionModal` — built once, for a real server `WeatherLogDto` — can be reused unchanged
 * to correct a queued-but-unsynced entry (`useWeatherLogOutbox.ts#correctableLocalOriginals`,
 * `WeatherPage.tsx`). **Client-display only**: this object is never sent to the server (the real
 * submit path is `useWeatherLogOutbox.ts#recordCorrection`, which resolves the id itself via
 * `parseWeatherLogTargetRef`/`resolveWeatherLogTargetId` at sync time — not from anything read back
 * off this synthetic DTO). `workStoppage` is approximated client-side (`impact !== 'NoImpact'`) —
 * the same rule §3.4 documents the server derives it by, but recomputed here rather than trusted from
 * an unconfirmed local write.
 */
export function pendingWeatherLogItemToDto(item: OutboxItem<WeatherLogOutboxPayload>): WeatherLogDto {
  const { fields } = item.payload
  return {
    id: toLocalWeatherLogTargetId(item.id),
    projectId: item.payload.projectId,
    logDate: fields.logDate,
    condition: fields.condition,
    conditionNote: fields.conditionNote,
    rainfallMm: fields.rainfallMm,
    impact: fields.impact,
    impactNote: fields.impactNote,
    hoursLost: fields.hoursLost,
    workStoppage: fields.impact !== 'NoImpact',
    entryKind: 'Original',
    correctsWeatherLogId: null,
    correctionReason: null,
    affectedActivityIds: fields.affectedActivityIds,
    recordedByUserId: item.ownerUserId,
    recordedAt: item.createdAt,
  }
}
