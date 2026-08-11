import { useEffect, useMemo, useState } from 'react'
import { useParams } from 'react-router-dom'
import { ChartCard } from '../../components'
import { RequireRole } from '../../routes'
import { EacVariantSelect } from './components/EacVariantSelect'
import { EvmMetricsGrid } from './components/EvmMetricsGrid'
import { SCurveChart } from './components/SCurveChart'
import { SetEacDefaultButton } from './components/SetEacDefaultButton'
import { EAC_NULL_REASON_LABELS, buildSCurvePoints, findVariantResult } from './evmSelectors'
import { useEvmData } from './useEvmData'
import { useEvmSnapshots } from './useEvmSnapshots'
import type { EacVariant } from './types'

/** Roles allowed to set the project's EAC default — mirrors `ProjectsController.SetEacDefault`'s
 * own `[Authorize(Roles = "PM,QS,Executive")]` exactly (ADR-0007(f)). Deliberately narrower than
 * `ProjectMasterCard.tsx`'s `EDIT_ROLES` (PM/QS/Executive/**Admin**) — ADR-0007(f) names exactly
 * these three roles and does not mention Admin, so Admin is not carried over here by assumption
 * (mirrors `ProjectsController.cs`'s own doc comment on the same deliberate narrowing). This is a
 * UX affordance gate only — the backend re-checks the role independently on every request
 * regardless of what renders here. */
const EAC_DEFAULT_ROLES = ['PM', 'QS', 'Executive'] as const

/**
 * S7-FE-01/02/03/04 "EVM S-Curve" screen (US-7.1/7.2/7.3/7.4*): the real 12-metric table, the
 * 3-option EAC variant selector, the "ตั้งเป็นค่าเริ่มต้นของโครงการ" action, and the S-Curve chart.
 *
 * `selectedVariant` is deliberately **local component state**, seeded from the server's own
 * `selectedVariant` on every fresh `GET .../evm` (mount or `reload()`), never re-synced on a
 * re-render that isn't a genuine new fetch — this is what makes switching the dropdown a zero
 * round-trip operation (design.md §1.1) while still reflecting a changed project default the next
 * time the page actually loads.
 */
export function EvmPage() {
  const { projectId } = useParams<{ projectId: string }>()
  const evmData = useEvmData(projectId ?? '')
  const snapshotsData = useEvmSnapshots(projectId ?? '')

  const [selectedVariant, setSelectedVariant] = useState<EacVariant | null>(null)
  const [persistedDefault, setPersistedDefault] = useState<EacVariant | null>(null)

  useEffect(() => {
    if (evmData.evm) {
      setSelectedVariant(evmData.evm.selectedVariant)
      setPersistedDefault(evmData.evm.selectedVariant)
    }
  }, [evmData.evm])

  const sCurvePoints = useMemo(
    () => (evmData.evm ? buildSCurvePoints(evmData.evm, snapshotsData.snapshots) : []),
    [evmData.evm, snapshotsData.snapshots],
  )

  if (!projectId) return null

  // Checked *before* the loading gate below: on a genuine load failure `evmData.evm` stays `null`
  // forever (nothing ever sets it), so a loading-gate condition that includes `!evmData.evm` would
  // otherwise match the error case too and this branch would never be reached — a real bug caught
  // by this sprint's own integration test, not a hypothetical.
  if (evmData.loadState === 'error') {
    return (
      <div
        role="alert"
        className="flex items-center justify-center rounded-card border border-border bg-surface py-16 text-xs text-danger"
      >
        {evmData.loadError ?? 'โหลดข้อมูล EVM ไม่สำเร็จ'}
      </div>
    )
  }

  if (evmData.loadState === 'loading' || !evmData.evm || selectedVariant === null || persistedDefault === null) {
    return (
      <div className="flex items-center justify-center rounded-card border border-border bg-surface py-16 text-xs text-text-faint">
        กำลังโหลดข้อมูล EVM...
      </div>
    )
  }

  const evm = evmData.evm
  const selectedVariantResult = findVariantResult(evm, selectedVariant)

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <EacVariantSelect variants={evm.variants} selected={selectedVariant} onChange={setSelectedVariant} />

        <RequireRole allowedRoles={[...EAC_DEFAULT_ROLES]}>
          <SetEacDefaultButton
            projectId={projectId}
            selectedVariant={selectedVariant}
            persistedDefault={persistedDefault}
            onSaved={setPersistedDefault}
          />
        </RequireRole>
      </div>

      {selectedVariantResult && !selectedVariantResult.computable && selectedVariantResult.reason && (
        <div className="rounded-card border border-border bg-surface px-3 py-2 text-[11px] text-text-muted">
          <span className="font-semibold text-navy">ตัวแปรที่เลือกยังคำนวณไม่ได้: </span>
          {EAC_NULL_REASON_LABELS[selectedVariantResult.reason]}
        </div>
      )}

      {evm.warnings.length > 0 && (
        <div role="alert" className="rounded-card border border-danger/30 bg-danger/5 px-3 py-2 text-[11px] text-danger">
          {evm.warnings.join(' · ')}
        </div>
      )}

      <ChartCard
        title="EVM S-Curve · PV / EV / AC + EAC Forecast"
        state={snapshotsData.loadState === 'loading' ? 'loading' : 'ready'}
      >
        <SCurveChart
          points={sCurvePoints}
          bac={evm.bac}
          forecastEac={selectedVariantResult?.eac ?? null}
          forecastUnavailableReason={selectedVariantResult?.reason ?? null}
        />
        {snapshotsData.loadState === 'error' && (
          <p role="alert" className="mt-2 text-[10.5px] text-danger">
            {snapshotsData.loadError}
          </p>
        )}
      </ChartCard>

      <EvmMetricsGrid evm={evm} selectedVariantResult={selectedVariantResult} />
    </div>
  )
}
