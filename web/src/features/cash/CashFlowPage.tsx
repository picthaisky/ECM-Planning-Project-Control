import { useParams } from 'react-router-dom'
import { ChartCard } from '../../components'
import { RequireRole } from '../../routes'
import { CashFlowBarChart } from './components/CashFlowBarChart'
import { CashFlowSummaryTiles } from './components/CashFlowSummaryTiles'
import { describeWarning } from './cashFlowSelectors'
import { useCashFlowData } from './useCashFlowData'

/** Roles allowed to view the Cash Flow screen — mirrors `CashFlowController`'s own
 * `[Authorize(Roles = "PM,QS,ProjectDirector,Executive,Admin")]` exactly (ADR-0013, same list
 * `EvmController`/`DashboardController` use — this response's `acCumulative`/period AC figures
 * expose the contractor's own spend, i.e. margin once set against receipts). The **read** itself is
 * server-side role-gated (unlike `features/wbs/`/`features/evm/`'s per-action gating — see
 * `DashboardPage.tsx`'s identical remark), so the whole screen is wrapped in `RequireRole` rather
 * than one button — a UX affordance only, the server enforces it regardless. */
const CASH_FLOW_ROLES = ['PM', 'QS', 'ProjectDirector', 'Executive', 'Admin'] as const

/**
 * S8-FE-02 "Cash Flow" screen (US-8.2): `GET .../cash-flow` (S8-BE-01) — the period bar chart plus
 * the cumulative summary tiles. Every number here traces to the EVM engine or a frozen
 * `EvmPeriodSnapshot` (the backend handler's own "no calculation outside the EVM engine" rule) —
 * this page never recomputes a domain formula, it only formats and lays out what the API returned.
 *
 * **ADR-0013 §5's hard requirement — receipts (`รับเงินสะสม`) and AC (`ต้นทุนเกิดขึ้นจริงสะสม`) must
 * never be merged into one figure:** enforced by construction here, not just by convention — they
 * are two separate top-level fields on `CashFlowResponseDto` (`receipts` vs `acCumulative`/
 * `periods[].acPeriod`), rendered by two different components (`CashFlowBarChart` never plots
 * receipts at all — there is no receipts series to plot while `isAvailable === false`;
 * `CashFlowSummaryTiles` renders them as two visually distinct tiles with different tones and an
 * explicit note). The *only* place the two ledgers are ever combined is `netCashPosition`, and only
 * when the backend itself has already computed it (today always `null`, since receipts don't exist
 * yet) — this page never subtracts the two client-side.
 */
export function CashFlowPage() {
  const { projectId } = useParams<{ projectId: string }>()
  if (!projectId) return null

  return (
    <RequireRole allowedRoles={[...CASH_FLOW_ROLES]}>
      <CashFlowContent projectId={projectId} />
    </RequireRole>
  )
}

function CashFlowContent({ projectId }: { projectId: string }) {
  const cashFlowData = useCashFlowData(projectId)

  // Checked *before* the loading gate below — same ordering rationale as `features/evm/EvmPage.tsx`/
  // `features/dashboard/DashboardPage.tsx`: on a genuine load failure `cashFlow` stays `null` forever.
  if (cashFlowData.loadState === 'error') {
    return (
      <div
        role="alert"
        className="flex items-center justify-center rounded-card border border-border bg-surface py-16 text-xs text-danger"
      >
        {cashFlowData.loadError ?? 'โหลดข้อมูล Cash Flow ไม่สำเร็จ'}
      </div>
    )
  }

  if (cashFlowData.loadState === 'loading' || !cashFlowData.cashFlow) {
    return (
      <div className="flex items-center justify-center rounded-card border border-border bg-surface py-16 text-xs text-text-faint">
        กำลังโหลดข้อมูล Cash Flow...
      </div>
    )
  }

  const cashFlow = cashFlowData.cashFlow

  return (
    <div className="flex flex-col gap-4" data-testid="cash-flow-page">
      {cashFlow.warnings.length > 0 && (
        <div
          role="alert"
          className="flex flex-col gap-1 rounded-card border border-danger/30 bg-danger/5 px-3 py-2 text-[11px] text-danger"
        >
          {cashFlow.warnings.map((code, index) => (
            <p key={`${code}-${index}`}>{describeWarning(code)}</p>
          ))}
        </div>
      )}

      <ChartCard title="กระแสเงินสดรายงวด (Cash Flow)" subtitle="PV แผน / EV ผลงาน / AC ต้นทุน รายงวด">
        <CashFlowBarChart periods={cashFlow.periods} />
      </ChartCard>

      <div className="rounded-card border border-border bg-surface p-4">
        <h3 className="mb-3 font-heading text-sm font-semibold text-navy">สรุปการเงินสะสม</h3>
        <CashFlowSummaryTiles cashFlow={cashFlow} />
      </div>
    </div>
  )
}
