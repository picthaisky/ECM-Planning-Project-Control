import { Button, OUTBOX_STATUS_LABELS, OUTBOX_STATUS_TONES, StatusPill } from '../../../components'
import { WEATHER_LOG_CORRECTION_OUTBOX_KIND, pendingWeatherLogItemToDto } from '../weatherOutbox'
import { formatWeatherDate } from '../weatherLabels'
import type { OutboxItem } from '../../../services/outbox'
import type { WeatherLogCorrectionOutboxPayload, WeatherLogOutboxPayload } from '../weatherOutbox'
import type { WeatherLogDto } from '../types'

export interface WeatherOutboxQueueProps {
  /** Both kinds together (Original + Correction/Retraction), this project only — see
   * `useWeatherLogOutbox.ts#reload`. */
  items: OutboxItem[]
  /** Local (not-yet-synced) Original items eligible for a correction —
   * `useWeatherLogOutbox.ts#correctableLocalOriginals`. */
  correctableLocalOriginals: OutboxItem<WeatherLogOutboxPayload>[]
  onRetry: () => void
  onRequestCorrection: (target: WeatherLogDto) => void
  canWrite: boolean
}

function kindLabel(kind: string): string {
  return kind === WEATHER_LOG_CORRECTION_OUTBOX_KIND ? 'รายการแก้ไข/เพิกถอน' : 'บันทึกใหม่'
}

/**
 * S13-FE-01's "เข้าคิวพร้อมสถานะรายรายการ" DoD for Weather Log — this device's own not-yet-synced
 * queue (both `kind`s), mirroring `features/photo/components/PhotoOutboxList.tsx`'s card-list
 * discipline but without a thumbnail (weather logs carry no binary attachment). `synced` items are
 * deliberately excluded here (unlike Photo, which has no server-side gallery at all) — once synced,
 * `WeatherLogTable` above this component is the authoritative, single place that row appears.
 *
 * The "แก้ไข" action on a correctable local Original is the concrete UI for the ordering problem
 * `weatherOutbox.ts` solves: it lets a site engineer queue a correction for a log they captured this
 * same offline session, before that log has ever reached the server.
 */
export function WeatherOutboxQueue({ items, correctableLocalOriginals, onRetry, onRequestCorrection, canWrite }: WeatherOutboxQueueProps) {
  const pending = items.filter((item) => item.status !== 'synced')
  const correctableIds = new Set(correctableLocalOriginals.map((item) => item.id))

  if (pending.length === 0) {
    return (
      <div className="flex h-24 items-center justify-center rounded-card border border-dashed border-border text-xs text-text-faint">
        ไม่มีรายการค้างซิงค์ในเครื่องนี้สำหรับโครงการนี้
      </div>
    )
  }

  return (
    <div className="space-y-2">
      {pending.map((item) => {
        const isCorrection = item.kind === WEATHER_LOG_CORRECTION_OUTBOX_KIND
        const logDate = isCorrection
          ? (item as OutboxItem<WeatherLogCorrectionOutboxPayload>).payload.fields.logDate
          : (item as OutboxItem<WeatherLogOutboxPayload>).payload.fields.logDate

        return (
          <div
            key={item.id}
            data-testid="weather-outbox-item"
            data-outbox-status={item.status}
            data-outbox-kind={item.kind}
            className="flex flex-wrap items-center gap-2 rounded-card border border-border bg-surface px-3 py-2"
          >
            <StatusPill label={OUTBOX_STATUS_LABELS[item.status]} tone={OUTBOX_STATUS_TONES[item.status]} />
            <span className="text-[11px] font-medium text-text">{kindLabel(item.kind)}</span>
            <span className="text-[11px] text-text-faint">{formatWeatherDate(logDate)}</span>

            {(item.status === 'failed' || item.status === 'conflict') && item.lastError && (
              <span className="w-full text-[10.5px] text-danger sm:w-auto sm:flex-1">{item.lastError}</span>
            )}

            <div className="ml-auto flex items-center gap-2">
              {item.status === 'failed' && (
                <button
                  type="button"
                  onClick={onRetry}
                  className="text-[10.5px] font-medium text-navy underline decoration-dotted hover:text-navy/70"
                >
                  ลองซิงค์ใหม่
                </button>
              )}
              {canWrite && !isCorrection && correctableIds.has(item.id) && (
                <Button
                  size="sm"
                  variant="secondary"
                  onClick={() => onRequestCorrection(pendingWeatherLogItemToDto(item as OutboxItem<WeatherLogOutboxPayload>))}
                >
                  แก้ไข/ยกเลิกรายการ
                </Button>
              )}
            </div>
          </div>
        )
      })}
    </div>
  )
}
