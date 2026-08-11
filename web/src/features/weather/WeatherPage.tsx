import { useState } from 'react'
import { useParams } from 'react-router-dom'
import { Button } from '../../components'
import { useAuthStore } from '../../store/authStore'
import type { UserRole } from '../../store/authStore'
import { EotEvaluationPanel } from './components/EotEvaluationPanel'
import { WeatherCorrectionModal } from './components/WeatherCorrectionModal'
import { WeatherLogTable } from './components/WeatherLogTable'
import { WeatherOutboxQueue } from './components/WeatherOutboxQueue'
import { WeatherRecordModal } from './components/WeatherRecordModal'
import { WeatherSummaryTiles } from './components/WeatherSummaryTiles'
import { useEotEvaluation } from './useEotEvaluation'
import { useWeatherLogOutbox } from './useWeatherLogOutbox'
import { useWeatherLogs } from './useWeatherLogs'
import type { WeatherLogDto } from './types'

/** Mirrors `ProjectWeatherLogsController.WriteRoles` exactly (PM/Planning/Site/QS/Admin) — the
 * established "who records field data" role set this codebase already uses for
 * `ProgressController`/`ImportController`'s analogous site-data-capture write paths. UX gate only;
 * the server re-checks independently. */
const WEATHER_WRITE_ROLES: readonly UserRole[] = ['PM', 'Planning', 'Site', 'QS', 'Admin']

/** Mirrors `ProjectEotEvaluationsController`'s own role gate exactly — narrower than the weather
 * write roles above: `Site` is excluded, since evaluating EOT produces an analytical schedule
 * opinion, not a raw site record. */
const EOT_EVALUATE_ROLES: readonly UserRole[] = ['PM', 'Planning', 'QS', 'Admin']

/**
 * S11-FE-01 "Weather Log" screen (US-11.1): real summary tiles + the immutable weather-log register
 * (`GET/POST .../weather-logs`, `POST .../weather-logs/{id}/corrections`) + the EOT evaluation panel
 * (`POST .../eot-evaluations`) — see `EotEvaluationPanel.tsx`'s own remarks for the ADR-0020
 * relabelling this screen carries out.
 *
 * S13-FE-01 (ADR-0005): both writes now go through `useWeatherLogOutbox` — the generic IndexedDB
 * outbox (`services/outbox/`) extended to a second `kind` (Original) plus a third
 * (Correction/Retraction), reusing the H-02 ownership seam unchanged. A write always enqueues first
 * (never a direct `recordWeatherLog`/`recordWeatherLogCorrection` call from this screen — see
 * `weatherOutbox.ts`), then attempts an immediate sync when the browser reports online, mirroring
 * `features/photo/usePhotoOutbox.ts`'s established shape exactly. `useWeatherLogActions.ts` (the
 * old, direct-API hook this replaces) is retired — keeping it around unused would be exactly the
 * "bypass the seam" foot-gun a future change could fall into.
 */
export function WeatherPage() {
  const { projectId } = useParams<{ projectId: string }>()
  const currentUserRole = useAuthStore((state) => state.claims?.role ?? null)

  const weatherLogs = useWeatherLogs(projectId ?? '')
  const outbox = useWeatherLogOutbox(projectId ?? '', () => void weatherLogs.reload())
  const eot = useEotEvaluation(projectId ?? '')

  const [recordModalOpen, setRecordModalOpen] = useState(false)
  const [correctionTarget, setCorrectionTarget] = useState<WeatherLogDto | null>(null)
  const [onlyUnattributed, setOnlyUnattributed] = useState(false)

  if (!projectId) return null

  const canWrite = currentUserRole !== null && WEATHER_WRITE_ROLES.includes(currentUserRole)
  const canEvaluateEot = currentUserRole !== null && EOT_EVALUATE_ROLES.includes(currentUserRole)
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
            ระบบจะพยายามซิงค์รายการที่ค้างอยู่ให้อัตโนมัติ
          </span>
        ) : (
          <span>
            อุปกรณ์นี้ไม่รองรับการซิงค์อัตโนมัติเบื้องหลัง (เช่น iOS/Safari) — ระบบจะซิงค์ให้ทันทีเมื่อเชื่อมต่ออินเทอร์เน็ต
            <span className="font-semibold"> ขณะเปิดหน้านี้ค้างไว้</span> หรือเมื่อกลับมาเปิดแอปอีกครั้งขณะออนไลน์
          </span>
        )}
        {pendingCount > 0 && (
          <span className="ml-2 font-medium text-warning-text">รอซิงค์อยู่ {pendingCount} รายการ</span>
        )}
      </div>

      <WeatherSummaryTiles
        logs={weatherLogs.logs}
        state={weatherLogs.loadState}
        errorMessage={weatherLogs.loadError ?? undefined}
        onFocusUnattributed={() => setOnlyUnattributed(true)}
      />

      <EotEvaluationPanel eot={eot} canEvaluate={canEvaluateEot} />

      <div className="overflow-hidden rounded-card border border-border bg-surface">
        <div className="flex flex-wrap items-center gap-2 px-4 py-3">
          <div className="font-heading text-[13.5px] font-semibold text-navy">
            บันทึกสภาพอากาศรายวัน (หลักฐานอ้างอิง EOT)
          </div>

          {onlyUnattributed && (
            <button
              type="button"
              onClick={() => setOnlyUnattributed(false)}
              className="rounded bg-warning-text/10 px-2 py-1 text-[10.5px] font-medium text-warning-text hover:bg-warning-text/20"
            >
              กำลังกรองเฉพาะรายการที่ยังไม่ระบุกิจกรรม — ล้างตัวกรอง
            </button>
          )}

          {canWrite && (
            <Button size="sm" className="ml-auto" onClick={() => setRecordModalOpen(true)}>
              + บันทึกวันนี้
            </Button>
          )}
        </div>

        <WeatherLogTable
          logs={weatherLogs.logs}
          state={weatherLogs.loadState}
          errorMessage={weatherLogs.loadError ?? undefined}
          onlyUnattributed={onlyUnattributed}
          canWrite={canWrite}
          onRequestCorrection={setCorrectionTarget}
        />
      </div>

      <div className="overflow-hidden rounded-card border border-border bg-surface p-4">
        <div className="flex items-center justify-between">
          <div className="font-heading text-[13px] font-semibold text-navy">คิวออฟไลน์ของอุปกรณ์นี้ (โครงการนี้)</div>
          <Button size="sm" variant="secondary" onClick={() => void outbox.syncNow()}>
            ซิงค์เดี๋ยวนี้
          </Button>
        </div>
        <div className="mt-3">
          <WeatherOutboxQueue
            items={outbox.items}
            correctableLocalOriginals={outbox.correctableLocalOriginals}
            onRetry={() => void outbox.syncNow()}
            onRequestCorrection={setCorrectionTarget}
            canWrite={canWrite}
          />
        </div>
      </div>

      <WeatherRecordModal
        isOpen={recordModalOpen}
        onClose={() => {
          outbox.clearActionError()
          setRecordModalOpen(false)
        }}
        busy={outbox.saving}
        errorMessage={outbox.actionError}
        onSubmit={(payload) => {
          void outbox.recordOriginal(payload).then((saved) => {
            if (saved) setRecordModalOpen(false)
          })
        }}
      />

      {correctionTarget && (
        <WeatherCorrectionModal
          isOpen
          target={correctionTarget}
          onClose={() => {
            outbox.clearActionError()
            setCorrectionTarget(null)
          }}
          busy={outbox.saving}
          errorMessage={outbox.actionError}
          onSubmit={(logId, payload) => {
            void outbox.recordCorrection(logId, payload).then((saved) => {
              if (saved) setCorrectionTarget(null)
            })
          }}
        />
      )}
    </div>
  )
}
