import { useState } from 'react'
import { useParams } from 'react-router-dom'
import { Button } from '../../components'
import { useAuthStore } from '../../store/authStore'
import type { UserRole } from '../../store/authStore'
import { EotEvaluationPanel } from './components/EotEvaluationPanel'
import { WeatherCorrectionModal } from './components/WeatherCorrectionModal'
import { WeatherLogTable } from './components/WeatherLogTable'
import { WeatherRecordModal } from './components/WeatherRecordModal'
import { WeatherSummaryTiles } from './components/WeatherSummaryTiles'
import { useEotEvaluation } from './useEotEvaluation'
import { useWeatherLogActions } from './useWeatherLogActions'
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
 */
export function WeatherPage() {
  const { projectId } = useParams<{ projectId: string }>()
  const currentUserRole = useAuthStore((state) => state.claims?.role ?? null)

  const weatherLogs = useWeatherLogs(projectId ?? '')
  const actions = useWeatherLogActions(projectId ?? '', () => void weatherLogs.reload())
  const eot = useEotEvaluation(projectId ?? '')

  const [recordModalOpen, setRecordModalOpen] = useState(false)
  const [correctionTarget, setCorrectionTarget] = useState<WeatherLogDto | null>(null)
  const [onlyUnattributed, setOnlyUnattributed] = useState(false)

  if (!projectId) return null

  const canWrite = currentUserRole !== null && WEATHER_WRITE_ROLES.includes(currentUserRole)
  const canEvaluateEot = currentUserRole !== null && EOT_EVALUATE_ROLES.includes(currentUserRole)

  return (
    <div className="flex flex-col gap-4">
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

      <WeatherRecordModal
        isOpen={recordModalOpen}
        onClose={() => {
          actions.clearActionError()
          setRecordModalOpen(false)
        }}
        busy={actions.busyAction === 'record'}
        errorMessage={actions.actionError}
        onSubmit={(payload) => {
          void actions.record(payload).then((saved) => {
            if (saved) setRecordModalOpen(false)
          })
        }}
      />

      {correctionTarget && (
        <WeatherCorrectionModal
          isOpen
          target={correctionTarget}
          onClose={() => {
            actions.clearActionError()
            setCorrectionTarget(null)
          }}
          busy={actions.busyAction === 'correct'}
          errorMessage={actions.actionError}
          onSubmit={(logId, payload) => {
            void actions.correct(logId, payload).then((saved) => {
              if (saved) setCorrectionTarget(null)
            })
          }}
        />
      )}
    </div>
  )
}
