import { useState } from 'react'
import { useParams } from 'react-router-dom'
import { Button, ChartCard } from '../../components'
import { useAuthStore } from '../../store/authStore'
import type { UserRole } from '../../store/authStore'
import { ManpowerCorrectionModal } from './components/ManpowerCorrectionModal'
import { ManpowerHistogramChart } from './components/ManpowerHistogramChart'
import { ManpowerKpiTiles } from './components/ManpowerKpiTiles'
import { ManpowerLogTable } from './components/ManpowerLogTable'
import { ManpowerRecordModal } from './components/ManpowerRecordModal'
import { useManpowerLogActions } from './useManpowerLogActions'
import { useManpowerOverview } from './useManpowerOverview'
import { useWorkCategories } from './useWorkCategories'
import { useWbsNodes } from './useWbsNodes'
import { useWeatherLogs } from './useWeatherLogs'
import type { ManpowerLogDto } from './types'

/** Mirrors `ProjectManpowerLogsController.WriteRoles` exactly (PM/Planning/Site/QS/Admin) — the same
 * "who records field data" role set `features/weather`/`features/photo` already use. */
const MANPOWER_WRITE_ROLES: readonly UserRole[] = ['PM', 'Planning', 'Site', 'QS', 'Admin']

/**
 * S12-FE-02 "Man / Equipment" screen (US-12.2, prototype ~line 536): KPI tiles + histogram + PI, per
 * the approved formula (domain-rules.md, decision D8) — **not** re-derived here; every number this
 * screen shows is either read verbatim from `GetProductivityIndexQueryHandler`'s response or a
 * client-side display transform (rounding/colour banding) that never changes what the number means.
 *
 * **Scope note.** There is still no `GET` list of raw manpower-log rows (a deliberate scope decision),
 * so the daily log table (`ManpowerLogTable.tsx`) shows only rows recorded in *this browser session*
 * (`recordedRows` below), not a full historical register. Every entity-reference field, however, is
 * now a real dropdown, so nobody ever types a GUID:
 * - `workCategoryId` (catalogue, `useWorkCategories`), `wbsNodeId` (WBS tree, `useWbsNodes`),
 *   `activityId` (a dependent dropdown of the selected node's activities, `useNodeActivities` inside
 *   the modals), and `relatedWeatherLogId` (`useWeatherLogs`). Each degrades to a raw-GUID text input
 *   when its source is empty/unavailable, so logging is never blocked — the graceful-degrade answer
 *   `features/weather/components/ActivityIdChips.tsx` established for the identical case.
 * - The equipment KPI tile renders "—" (`ManpowerKpiTiles.tsx`) rather than a fabricated utilisation
 *   figure.
 * - The histogram's bar colour uses real per-day manning (headcount) variance, not a manufactured
 *   planned-*hours* overlay (`ManpowerHistogramChart.tsx`) — `ManpowerPlan`/derived plan-hours have
 *   no read endpoint either.
 *
 * None of this affects the one thing decision D8 and this task both gate on: $PI$ itself, its null
 * reasons, and the manning-ratio/PI distinction are all real, live, correctly-labelled backend data.
 */
export function ManeqPage() {
  const { projectId } = useParams<{ projectId: string }>()
  const currentUserRole = useAuthStore((state) => state.claims?.role ?? null)

  const overview = useManpowerOverview(projectId ?? '')
  const workCategories = useWorkCategories(projectId ?? '')
  const wbsNodes = useWbsNodes(projectId ?? '')
  const weatherLogs = useWeatherLogs(projectId ?? '')
  const [recordedRows, setRecordedRows] = useState<ManpowerLogDto[]>([])
  const actions = useManpowerLogActions(projectId ?? '', () => void overview.reload())

  const [recordModalOpen, setRecordModalOpen] = useState(false)
  const [correctionTarget, setCorrectionTarget] = useState<ManpowerLogDto | null>(null)

  if (!projectId) return null

  const canWrite = currentUserRole !== null && MANPOWER_WRITE_ROLES.includes(currentUserRole)

  function upsertRecordedRow(row: ManpowerLogDto) {
    setRecordedRows((prev) => {
      // A correction/retraction targets `correctsLogId` — replace that row's place in the session
      // table with the new one rather than appending a second row for the same logical entry, so the
      // table always shows the *current* value of each recorded key (the append-only chain still
      // exists server-side regardless — this is a display choice, not a storage one).
      const supersededId = row.correctsLogId
      const withoutSuperseded = supersededId ? prev.filter((existing) => existing.id !== supersededId) : prev
      return [row, ...withoutSuperseded]
    })
  }

  return (
    <div className="flex flex-col gap-4">
      <ManpowerKpiTiles today={overview.today} monthToDate={overview.monthToDate} cumulativePi={overview.cumulative} />

      <ChartCard
        title="Histogram กำลังคน 7 วันล่าสุด + Productivity Index"
        subtitle="PI วัดประสิทธิภาพชั่วโมงแรงงานเท่านั้น — ไม่ใช่ตัวเดียวกับ CPI ซึ่งรวมค่าวัสดุ ผู้รับเหมาช่วง และเครื่องจักร ทั้งสองค่าต่างกันได้โดยไม่ถือว่าผิด"
        state={overview.histogram.state === 'loading' ? 'loading' : overview.histogram.state === 'error' ? 'error' : overview.histogram.data && overview.histogram.data.length === 0 ? 'empty' : 'ready'}
        errorMessage={overview.histogram.error ?? undefined}
      >
        <ManpowerHistogramChart points={overview.histogram.data ?? []} />
      </ChartCard>

      <div className="overflow-hidden rounded-card border border-border bg-surface">
        <div className="flex flex-wrap items-center gap-2 px-4 py-3">
          <div>
            <div className="font-heading text-[13.5px] font-semibold text-navy">บันทึกกำลังคน/เครื่องจักรรายวัน</div>
            <div className="text-[10px] text-text-faint">
              แสดงเฉพาะรายการที่บันทึกในเซสชันนี้ — ระบบหลังบ้านยังไม่มี endpoint สำหรับดึงประวัติทั้งหมด
            </div>
          </div>
          {canWrite && (
            <Button size="sm" className="ml-auto" onClick={() => setRecordModalOpen(true)}>
              + บันทึกวันนี้
            </Button>
          )}
        </div>

        <ManpowerLogTable rows={recordedRows} canWrite={canWrite} onRequestCorrection={setCorrectionTarget} />
      </div>

      <ManpowerRecordModal
        isOpen={recordModalOpen}
        projectId={projectId}
        onClose={() => {
          actions.clearActionError()
          setRecordModalOpen(false)
        }}
        busy={actions.busyAction === 'record'}
        errorMessage={actions.actionError}
        workCategories={workCategories}
        wbsNodes={wbsNodes}
        weatherLogs={weatherLogs}
        onSubmit={(payload) => {
          void actions.record(payload).then((saved) => {
            if (saved) {
              upsertRecordedRow(saved)
              setRecordModalOpen(false)
            }
          })
        }}
      />

      {correctionTarget && (
        <ManpowerCorrectionModal
          isOpen
          projectId={projectId}
          target={correctionTarget}
          onClose={() => {
            actions.clearActionError()
            setCorrectionTarget(null)
          }}
          busy={actions.busyAction === 'correct'}
          errorMessage={actions.actionError}
          workCategories={workCategories}
          wbsNodes={wbsNodes}
          weatherLogs={weatherLogs}
          onSubmit={(logId, payload) => {
            void actions.correct(logId, payload).then((saved) => {
              if (saved) {
                upsertRecordedRow(saved)
                setCorrectionTarget(null)
              }
            })
          }}
        />
      )}
    </div>
  )
}
