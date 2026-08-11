import { useState } from 'react'
import { useParams } from 'react-router-dom'
import { Button } from '../../components'
import { useAuthStore } from '../../store/authStore'
import type { UserRole } from '../../store/authStore'
import { BaselineComparisonTable } from './components/BaselineComparisonTable'
import { BaselineListPanel } from './components/BaselineListPanel'
import { BaselineSummaryTiles } from './components/BaselineSummaryTiles'
import { CaptureBaselineModal } from './components/CaptureBaselineModal'
import { useBaselineComparison } from './useBaselineComparison'
import { useBaselines } from './useBaselines'

/** Mirrors `BaselinesController.Capture`/`.Activate`'s `[Authorize(Roles = "PM,Planning,Admin")]`
 * exactly (same audience as `CpmController`'s "คำนวณ CPM ใหม่" gate — a scheduling/reference-
 * setting operation, not a Site/QS/Executive one). */
const BASELINE_WRITE_ROLES: readonly UserRole[] = ['PM', 'Planning', 'Admin']

/**
 * S14-FE-01 "Baseline" screen (US-14.1/US-14.2): manage multiple captured baselines, set one
 * Active, and the current-vs-baseline comparison table.
 *
 * **Two gaps this screen was built against, both deliberately, not accidentally:**
 * 1. There is no `GET .../baselines` list endpoint yet — `BaselineListPanel`/`useBaselines.ts` fall
 *    back to a session-local list built from `capture`/`activate` responses, with an honest inline
 *    note, rather than blocking the screen. The comparison table does *not* depend on this gap at
 *    all (`compareBaseline` defaults server-side to the active baseline).
 * 2. The prototype's 4th comparison tile ("Critical Path เปลี่ยนเส้นทาง") is not reconstructable
 *    from what `BaselineActivitySnapshot` persists — `BaselineSummaryTiles`/`BaselineComparisonTable`
 *    show 3 aggregate tiles plus a real, honest per-row *current* criticality badge instead of
 *    fabricating the aggregate claim.
 *
 * Read access (the comparison table) is not client-side role-gated ahead of the fetch — same
 * established precedent as `EvmPage`/`CashFlowPage`: the backend's own role check (PM/QS/
 * ProjectDirector/Executive/Admin) is the real boundary, and a disallowed role simply sees this
 * hook's own Thai 403 message. Only the write affordances (capture/activate) are hidden client-side
 * for roles that could never use them.
 */
export function BaselinePage() {
  const { projectId } = useParams<{ projectId: string }>()
  const role = useAuthStore((state) => state.claims?.role)
  const canWrite = role !== undefined && role !== null && BASELINE_WRITE_ROLES.includes(role)

  const [captureModalOpen, setCaptureModalOpen] = useState(false)
  const [selectedBaselineId, setSelectedBaselineId] = useState<string | null>(null)

  const baselinesData = useBaselines(projectId ?? '')
  const comparisonData = useBaselineComparison(projectId ?? '', selectedBaselineId ?? undefined)

  if (!projectId) return null

  async function handleCapture(name: string) {
    const created = await baselinesData.capture(name)
    if (created) setCaptureModalOpen(false)
  }

  async function handleActivate(baselineId: string) {
    const activated = await baselinesData.activate(baselineId)
    // The comparison table's default (no explicit `selectedBaselineId`) already targets whichever
    // baseline is active server-side — reload so it reflects the just-activated one immediately,
    // without the user having to manually refresh.
    if (activated && selectedBaselineId === null) {
      void comparisonData.reload()
    }
  }

  const comparisonState: 'ready' | 'loading' | 'error' =
    comparisonData.loadState === 'no-active-baseline' ? 'error' : comparisonData.loadState

  return (
    <div className="flex flex-col gap-4">
      {comparisonData.loadState === 'no-active-baseline' ? (
        <div className="rounded-card border border-border bg-surface px-4 py-3 text-[12.5px] text-text-muted">
          โครงการนี้ยังไม่มี Baseline ที่ Active — บันทึกและตั้งเป็น Active อย่างน้อยหนึ่งชุดก่อนจึงจะเปรียบเทียบได้
        </div>
      ) : (
        <BaselineSummaryTiles
          comparison={comparisonData.comparison}
          state={comparisonState}
          errorMessage={comparisonData.loadError ?? undefined}
        />
      )}

      <div className="grid grid-cols-1 items-start gap-4 lg:grid-cols-[1fr_2fr]">
        <BaselineListPanel
          baselines={baselinesData.baselines}
          loadState={baselinesData.loadState}
          listAvailable={baselinesData.listAvailable}
          actionState={baselinesData.actionState}
          actionError={baselinesData.actionError}
          canWrite={canWrite}
          onOpenCapture={() => setCaptureModalOpen(true)}
          onActivate={(id) => void handleActivate(id)}
          selectedBaselineId={selectedBaselineId}
          onSelect={setSelectedBaselineId}
        />

        <div className="flex flex-col gap-2">
          <div className="flex items-center justify-between">
            <div className="font-heading text-sm font-semibold text-navy">
              เปรียบเทียบแผนปัจจุบัน{comparisonData.comparison ? ` vs ${comparisonData.comparison.baselineName}` : ''}
            </div>
            {selectedBaselineId !== null && (
              <Button size="sm" variant="secondary" onClick={() => setSelectedBaselineId(null)}>
                กลับไปเทียบกับ Baseline ที่ Active
              </Button>
            )}
          </div>

          <BaselineComparisonTable
            activities={comparisonData.comparison?.activities ?? []}
            state={comparisonState}
            errorMessage={comparisonData.loadError ?? undefined}
          />
        </div>
      </div>

      <CaptureBaselineModal
        isOpen={captureModalOpen}
        onClose={() => {
          baselinesData.clearActionError()
          setCaptureModalOpen(false)
        }}
        busy={baselinesData.actionState === 'busy'}
        errorMessage={baselinesData.actionError}
        onCapture={(name) => void handleCapture(name)}
      />
    </div>
  )
}
