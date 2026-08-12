import { useState } from 'react'
import { Link } from 'react-router-dom'
import { useAuthStore } from '../../store/authStore'
import { useProjectStore } from '../../store/projectStore'
import { cx } from '../../utils/cx'
import { ApprovalPolicyEditorForm } from './components/ApprovalPolicyEditorForm'
import { PolicyHistoryTimeline } from './components/PolicyHistoryTimeline'
import { RoutingSimulatorPanel } from './components/RoutingSimulatorPanel'
import { useApprovalPolicy } from './useApprovalPolicy'
import { useApprovalPolicyHistory } from './useApprovalPolicyHistory'
import { useRoutingSimulator } from './useRoutingSimulator'
import { useUpdateApprovalPolicy } from './useUpdateApprovalPolicy'
import type { ApprovalDocumentType } from './types'

const DOCUMENT_TYPE_TABS: { id: ApprovalDocumentType; label: string }[] = [
  { id: 'PaymentCertificate', label: 'Payment Certificate' },
  { id: 'VariationOrder', label: 'Variation Order' },
]

interface DocumentTypeEditorProps {
  tenantId: string
  documentType: ApprovalDocumentType
}

/** Owns the load+save+history+simulate hooks for exactly one document type. Rendered with
 * `key={documentType}` by `TenantAdminPage` so switching tabs remounts it cleanly — a "saved v4"
 * banner (or a save error, or a stale simulator result) from the *other* document type's policy
 * must never bleed across the tab switch. */
function DocumentTypeEditor({ tenantId, documentType }: DocumentTypeEditorProps) {
  const policyData = useApprovalPolicy(tenantId, documentType)
  const updateData = useUpdateApprovalPolicy(tenantId, documentType)
  const historyData = useApprovalPolicyHistory(tenantId, documentType)
  const simulatorData = useRoutingSimulator(tenantId, documentType)
  const defaultProjectId = useProjectStore((state) => state.currentProjectId)

  async function handleSave(payload: Parameters<typeof updateData.save>[0]) {
    const updated = await updateData.save(payload)
    if (updated) {
      await policyData.reload()
      // A save creates Version+1 (S9-BE-06) — the timeline must reflect it immediately, and any
      // simulator result on screen was resolved against a now-superseded version, so it is cleared
      // rather than left displayed with a stale "v{N}" label.
      await historyData.reload()
      simulatorData.reset()
    }
  }

  return (
    <div className="flex flex-col gap-4">
      <div className="rounded-card border border-border bg-surface p-5">
        <ApprovalPolicyEditorForm
          documentType={documentType}
          policy={policyData.policy}
          loadState={policyData.loadState}
          loadError={policyData.loadError}
          saveState={updateData.saveState}
          saveError={updateData.saveError}
          savedVersion={updateData.savedVersion}
          onDismissSavedVersion={updateData.clearSavedVersion}
          onSave={(payload) => void handleSave(payload)}
        />
      </div>

      <PolicyHistoryTimeline
        documentType={documentType}
        entries={historyData.entries}
        loadState={historyData.loadState}
        loadError={historyData.loadError}
        onRetry={() => void historyData.reload()}
      />

      <RoutingSimulatorPanel
        documentType={documentType}
        defaultProjectId={defaultProjectId}
        state={simulatorData.state}
        error={simulatorData.error}
        result={simulatorData.result}
        onSimulate={(payload) => void simulatorData.simulate(payload)}
      />
    </div>
  )
}

/**
 * S9-FE-03 "Tenant Admin — Approval Policy" screen (US-9.4) — deliberately **not** one of the
 * 13-screen project nav's `NAV_ENTRIES` (`navConfig.ts`); it renders its own minimal header rather
 * than the project `AppShell`/`Sidebar`, because it configures a *tenant*-wide policy, never a
 * single project's data (design.md §4: "reachable only with role = Admin, from a tenant-level entry
 * point outside the 13-screen project nav"). Reached via the Topbar's Admin-only "Tenant Admin" link
 * (`components/layout/Topbar.tsx`) and gated at the route level by `RequireRole` (`routes/
 * AppRoutes.tsx`) — a non-Admin hitting `/tenant-admin` directly gets the real `ForbiddenPage` (403),
 * the same component/state every other role gate in this app uses; the backend enforces the actual
 * boundary independently (`TenantApprovalPoliciesController`'s class-level `[Authorize(Roles =
 * "Admin")]`) regardless of this client-side guard.
 *
 * S15-FE-01 extends this screen (per document-type tab, via `DocumentTypeEditor`) with the version
 * timeline (`PolicyHistoryTimeline`, `GET .../{documentType}/history`) and the routing simulator
 * (`RoutingSimulatorPanel`, `POST .../{documentType}/simulate`) below the existing editor — both
 * `Admin`-only, tenant-scoped `TenantApprovalPoliciesController` actions, so they inherit the exact
 * same `RequireRole`/`[Authorize(Roles = "Admin")]` defence-in-depth this route already has; no new
 * route or guard was needed.
 */
export function TenantAdminPage() {
  const tenantId = useAuthStore((state) => state.claims?.tenantId ?? null)
  const [documentType, setDocumentType] = useState<ApprovalDocumentType>('PaymentCertificate')

  if (!tenantId) return null

  return (
    <div className="min-h-screen bg-bg">
      <header className="flex items-center gap-3 border-b border-border bg-surface px-6 py-4">
        <div
          aria-hidden="true"
          className="grid h-8 w-8 flex-none place-items-center rounded-md bg-gold font-heading text-xs font-bold text-navy"
        >
          ECM
        </div>
        <div>
          <div className="font-heading text-sm font-semibold text-navy">
            Tenant Admin — นโยบายการอนุมัติ (Approval Policy)
          </div>
          <div className="text-[11px] text-text-faint">ตั้งค่าระดับองค์กร ไม่ผูกกับโครงการใดโครงการหนึ่ง</div>
        </div>
        <Link to="/" className="ml-auto text-[11.5px] text-navy underline decoration-dotted hover:text-navy/70">
          กลับสู่โครงการ
        </Link>
      </header>

      <main className="mx-auto max-w-5xl px-6 py-6">
        <div className="mb-4 flex gap-2" role="tablist" aria-label="ประเภทเอกสาร">
          {DOCUMENT_TYPE_TABS.map((tab) => (
            <button
              key={tab.id}
              type="button"
              role="tab"
              aria-selected={documentType === tab.id}
              onClick={() => setDocumentType(tab.id)}
              className={cx(
                'rounded-md px-3 py-1.5 text-xs font-semibold',
                documentType === tab.id
                  ? 'bg-navy text-white'
                  : 'border border-border text-text-muted hover:border-navy hover:text-navy',
              )}
            >
              {tab.label}
            </button>
          ))}
        </div>

        <DocumentTypeEditor key={documentType} tenantId={tenantId} documentType={documentType} />
      </main>
    </div>
  )
}
