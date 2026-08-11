import { beforeEach, describe, expect, it, vi } from 'vitest'
import {
  WEATHER_LOG_CORRECTION_OUTBOX_KIND,
  WEATHER_LOG_CORRECTION_TARGET_MISSING_MESSAGE,
  WEATHER_LOG_CORRECTION_WAITING_MESSAGE,
  WEATHER_LOG_OUTBOX_KIND,
  createWeatherLogCorrectionUploader,
  parseWeatherLogTargetRef,
  resolveWeatherLogTargetId,
  toLocalWeatherLogTargetId,
  uploadWeatherLogOutboxItem,
} from './weatherOutbox'
import { recordWeatherLog, recordWeatherLogCorrection, WeatherApiError } from './api'
import { createOutboxStore, createInMemoryOutboxStorage, OutboxConflictError } from '../../services/outbox'
import type { OutboxItem } from '../../services/outbox'
import type { WeatherLogCorrectionOutboxPayload, WeatherLogOutboxPayload } from './weatherOutbox'
import type { RecordWeatherLogCorrectionPayload, RecordWeatherLogPayload } from './types'

vi.mock('./api', () => ({
  recordWeatherLog: vi.fn(),
  recordWeatherLogCorrection: vi.fn(),
  WeatherApiError: class WeatherApiError extends Error {
    status?: number
    code?: string
    constructor(message: string, status?: number, code?: string) {
      super(message)
      this.name = 'WeatherApiError'
      this.status = status
      this.code = code
    }
  },
}))

const OWNER = { userId: 'user-a', tenantId: 'tenant-1' }
const SAMPLE_FIELDS: RecordWeatherLogPayload = {
  logDate: '2026-08-11T00:00:00.000Z',
  condition: 'HeavyRain',
  conditionNote: null,
  rainfallMm: '40.00',
  impact: 'FullStoppage',
  impactNote: null,
  hoursLost: '8.00',
  affectedActivityIds: [],
}
const SAMPLE_CORRECTION_FIELDS: RecordWeatherLogCorrectionPayload = {
  entryKind: 'Correction',
  correctionReason: 'พิมพ์ผิด',
  ...SAMPLE_FIELDS,
}

function makeStore() {
  let tick = 0
  let id = 0
  const storage = createInMemoryOutboxStorage()
  return createOutboxStore({
    storage,
    getOwner: () => OWNER,
    now: () => new Date(2026, 7, 11, 9, 0, (tick += 1)).toISOString(),
    generateId: () => `id-${(id += 1)}`,
  })
}

function makeOriginalItem(overrides: Partial<OutboxItem<WeatherLogOutboxPayload>> = {}): OutboxItem<WeatherLogOutboxPayload> {
  return {
    id: 'item-1',
    kind: WEATHER_LOG_OUTBOX_KIND,
    idempotencyKey: 'idem-1',
    ownerUserId: 'user-a',
    ownerTenantId: 'tenant-1',
    payload: { projectId: 'project-1', fields: SAMPLE_FIELDS },
    blob: null,
    blobFileName: null,
    blobContentType: null,
    status: 'syncing',
    attemptCount: 0,
    lastError: null,
    createdAt: '2026-08-11T09:00:00.000Z',
    updatedAt: '2026-08-11T09:00:00.000Z',
    syncedAt: null,
    serverId: null,
    ...overrides,
  }
}

function makeCorrectionItem(
  overrides: Partial<OutboxItem<WeatherLogCorrectionOutboxPayload>> = {},
): OutboxItem<WeatherLogCorrectionOutboxPayload> {
  return {
    id: 'item-2',
    kind: WEATHER_LOG_CORRECTION_OUTBOX_KIND,
    idempotencyKey: 'idem-2',
    ownerUserId: 'user-a',
    ownerTenantId: 'tenant-1',
    payload: {
      projectId: 'project-1',
      target: { type: 'server', logId: 'server-log-1' },
      fields: SAMPLE_CORRECTION_FIELDS,
    },
    blob: null,
    blobFileName: null,
    blobContentType: null,
    status: 'syncing',
    attemptCount: 0,
    lastError: null,
    createdAt: '2026-08-11T09:05:00.000Z',
    updatedAt: '2026-08-11T09:05:00.000Z',
    syncedAt: null,
    serverId: null,
    ...overrides,
  }
}

describe('features/weather/weatherOutbox', () => {
  beforeEach(() => {
    vi.mocked(recordWeatherLog).mockReset()
    vi.mocked(recordWeatherLogCorrection).mockReset()
  })

  describe('local target id round trip', () => {
    it('toLocalWeatherLogTargetId/parseWeatherLogTargetRef round-trip a local outbox item id', () => {
      const id = toLocalWeatherLogTargetId('outbox-item-42')
      expect(parseWeatherLogTargetRef(id)).toEqual({ type: 'local', outboxItemId: 'outbox-item-42' })
    })

    it('parseWeatherLogTargetRef treats a plain (real) GUID-shaped id as a server target', () => {
      expect(parseWeatherLogTargetRef('3fa85f64-5717-4562-b3fc-2c963f66afa6')).toEqual({
        type: 'server',
        logId: '3fa85f64-5717-4562-b3fc-2c963f66afa6',
      })
    })
  })

  describe('uploadWeatherLogOutboxItem', () => {
    it('forwards projectId/fields and returns the server id', async () => {
      vi.mocked(recordWeatherLog).mockResolvedValueOnce({
        id: 'server-log-1',
        projectId: 'project-1',
        logDate: SAMPLE_FIELDS.logDate,
        condition: SAMPLE_FIELDS.condition,
        conditionNote: null,
        rainfallMm: SAMPLE_FIELDS.rainfallMm,
        impact: SAMPLE_FIELDS.impact,
        impactNote: null,
        hoursLost: SAMPLE_FIELDS.hoursLost,
        workStoppage: true,
        entryKind: 'Original',
        correctsWeatherLogId: null,
        correctionReason: null,
        affectedActivityIds: [],
        recordedByUserId: 'user-1',
        recordedAt: '2026-08-11T09:00:00.000Z',
      })

      const result = await uploadWeatherLogOutboxItem(makeOriginalItem())

      expect(result).toEqual({ serverId: 'server-log-1' })
      expect(recordWeatherLog).toHaveBeenCalledWith('project-1', SAMPLE_FIELDS, 'idem-1')
    })

    it('maps a 409 IdempotencyPayloadMismatch to OutboxConflictError', async () => {
      vi.mocked(recordWeatherLog).mockRejectedValueOnce(
        new WeatherApiError('รายการนี้เคยถูกส่งไปแล้วด้วยข้อมูลที่ต่างจากครั้งนี้', 409, 'IdempotencyPayloadMismatch'),
      )

      const error = await uploadWeatherLogOutboxItem(makeOriginalItem()).catch((e: unknown) => e)
      expect(error).toBeInstanceOf(OutboxConflictError)
    })

    it('leaves a non-conflict error (e.g. validation) as an ordinary retryable throw', async () => {
      vi.mocked(recordWeatherLog).mockRejectedValueOnce(
        new WeatherApiError('พบรหัสกิจกรรมที่ไม่อยู่ในโครงการนี้', 400, 'WeatherLogUnknownActivity'),
      )

      const error = await uploadWeatherLogOutboxItem(makeOriginalItem()).catch((e: unknown) => e)
      expect(error).not.toBeInstanceOf(OutboxConflictError)
      expect(error).toBeInstanceOf(WeatherApiError)
    })
  })

  describe('resolveWeatherLogTargetId', () => {
    it('a server target resolves immediately, without touching the store', async () => {
      const store = makeStore()
      const resolution = await resolveWeatherLogTargetId(store, { type: 'server', logId: 'server-log-9' })
      expect(resolution).toEqual({ status: 'ready', logId: 'server-log-9' })
    })

    it('a local target that has not synced yet resolves to pending', async () => {
      const store = makeStore()
      const original = await store.enqueue<WeatherLogOutboxPayload>({
        kind: WEATHER_LOG_OUTBOX_KIND,
        payload: { projectId: 'project-1', fields: SAMPLE_FIELDS },
      })

      const resolution = await resolveWeatherLogTargetId(store, { type: 'local', outboxItemId: original.id })
      expect(resolution).toEqual({ status: 'pending' })
    })

    it('a local target that has already synced (e.g. earlier in the same flush pass) resolves to its real server id', async () => {
      const store = makeStore()
      const original = await store.enqueue<WeatherLogOutboxPayload>({
        kind: WEATHER_LOG_OUTBOX_KIND,
        payload: { projectId: 'project-1', fields: SAMPLE_FIELDS },
      })
      await store.markSynced(original.id, 'server-log-resolved')

      const resolution = await resolveWeatherLogTargetId(store, { type: 'local', outboxItemId: original.id })
      expect(resolution).toEqual({ status: 'ready', logId: 'server-log-resolved' })
    })

    it('a local target no longer present in this owner\'s outbox resolves to missing', async () => {
      const store = makeStore()
      const resolution = await resolveWeatherLogTargetId(store, { type: 'local', outboxItemId: 'gone' })
      expect(resolution).toEqual({ status: 'missing' })
    })
  })

  describe('createWeatherLogCorrectionUploader', () => {
    it('resolves a server target and forwards to recordWeatherLogCorrection', async () => {
      const store = makeStore()
      vi.mocked(recordWeatherLogCorrection).mockResolvedValueOnce({
        id: 'server-correction-1',
        projectId: 'project-1',
        logDate: SAMPLE_FIELDS.logDate,
        condition: SAMPLE_FIELDS.condition,
        conditionNote: null,
        rainfallMm: SAMPLE_FIELDS.rainfallMm,
        impact: SAMPLE_FIELDS.impact,
        impactNote: null,
        hoursLost: SAMPLE_FIELDS.hoursLost,
        workStoppage: true,
        entryKind: 'Correction',
        correctsWeatherLogId: 'server-log-1',
        correctionReason: 'พิมพ์ผิด',
        affectedActivityIds: [],
        recordedByUserId: 'user-1',
        recordedAt: '2026-08-11T09:05:00.000Z',
      })

      const uploader = createWeatherLogCorrectionUploader(store)
      const result = await uploader(makeCorrectionItem())

      expect(result).toEqual({ serverId: 'server-correction-1' })
      expect(recordWeatherLogCorrection).toHaveBeenCalledWith('project-1', 'server-log-1', SAMPLE_CORRECTION_FIELDS, 'idem-2')
    })

    it('throws the Thai "waiting for original" message (not opaque) when the local target has not synced yet, and never calls the API', async () => {
      const store = makeStore()
      const original = await store.enqueue<WeatherLogOutboxPayload>({
        kind: WEATHER_LOG_OUTBOX_KIND,
        payload: { projectId: 'project-1', fields: SAMPLE_FIELDS },
      })
      const correctionItem = makeCorrectionItem({
        payload: { projectId: 'project-1', target: { type: 'local', outboxItemId: original.id }, fields: SAMPLE_CORRECTION_FIELDS },
      })

      const uploader = createWeatherLogCorrectionUploader(store)
      await expect(uploader(correctionItem)).rejects.toThrow(WEATHER_LOG_CORRECTION_WAITING_MESSAGE)
      expect(recordWeatherLogCorrection).not.toHaveBeenCalled()
    })

    it('resolves automatically once the local target original has synced', async () => {
      const store = makeStore()
      const original = await store.enqueue<WeatherLogOutboxPayload>({
        kind: WEATHER_LOG_OUTBOX_KIND,
        payload: { projectId: 'project-1', fields: SAMPLE_FIELDS },
      })
      await store.markSynced(original.id, 'server-log-now-known')
      vi.mocked(recordWeatherLogCorrection).mockResolvedValueOnce({
        id: 'server-correction-2',
        projectId: 'project-1',
        logDate: SAMPLE_FIELDS.logDate,
        condition: SAMPLE_FIELDS.condition,
        conditionNote: null,
        rainfallMm: SAMPLE_FIELDS.rainfallMm,
        impact: SAMPLE_FIELDS.impact,
        impactNote: null,
        hoursLost: SAMPLE_FIELDS.hoursLost,
        workStoppage: true,
        entryKind: 'Correction',
        correctsWeatherLogId: 'server-log-now-known',
        correctionReason: 'พิมพ์ผิด',
        affectedActivityIds: [],
        recordedByUserId: 'user-1',
        recordedAt: '2026-08-11T09:05:00.000Z',
      })
      const correctionItem = makeCorrectionItem({
        payload: { projectId: 'project-1', target: { type: 'local', outboxItemId: original.id }, fields: SAMPLE_CORRECTION_FIELDS },
      })

      const uploader = createWeatherLogCorrectionUploader(store)
      const result = await uploader(correctionItem)

      expect(result).toEqual({ serverId: 'server-correction-2' })
      expect(recordWeatherLogCorrection).toHaveBeenCalledWith('project-1', 'server-log-now-known', SAMPLE_CORRECTION_FIELDS, 'idem-2')
    })

    it('throws the Thai "target missing" message when the local target no longer exists on this device', async () => {
      const store = makeStore()
      const correctionItem = makeCorrectionItem({
        payload: { projectId: 'project-1', target: { type: 'local', outboxItemId: 'gone-forever' }, fields: SAMPLE_CORRECTION_FIELDS },
      })

      const uploader = createWeatherLogCorrectionUploader(store)
      await expect(uploader(correctionItem)).rejects.toThrow(WEATHER_LOG_CORRECTION_TARGET_MISSING_MESSAGE)
      expect(recordWeatherLogCorrection).not.toHaveBeenCalled()
    })

    it('maps a 409 IdempotencyPayloadMismatch to OutboxConflictError once the target resolves', async () => {
      const store = makeStore()
      vi.mocked(recordWeatherLogCorrection).mockRejectedValueOnce(
        new WeatherApiError('รายการนี้เคยถูกส่งไปแล้วด้วยข้อมูลที่ต่างจากครั้งนี้', 409, 'IdempotencyPayloadMismatch'),
      )

      const uploader = createWeatherLogCorrectionUploader(store)
      const error = await uploader(makeCorrectionItem()).catch((e: unknown) => e)

      expect(error).toBeInstanceOf(OutboxConflictError)
    })
  })
})
