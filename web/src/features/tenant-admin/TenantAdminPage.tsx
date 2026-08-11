import { useState } from 'react'
import { Link } from 'react-router-dom'
import { useAuthStore } from '../../store/authStore'
import { cx } from '../../utils/cx'
import { ApprovalPolicyEditorForm } from './components/ApprovalPolicyEditorForm'
import { useApprovalPolicy } from './useApprovalPolicy'
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

/** Owns the load+save hooks for exactly one document type. Rendered with `key={documentType}` by
 * `TenantAdminPage` so switching tabs remounts it cleanly — a "saved v4" banner (or a save error)
 * from the *other* document type's policy must never bleed across the tab switch. */
function DocumentTypeEditor({ tenantId, documentType }: DocumentTypeEditorProps) {
  const policyData = useApprovalPolicy(tenantId, documentType)
  const updateData = useUpdateApprovalPolicy(tenantId, documentType)

  async function handleSave(payload: Parameters<typeof updateData.save>[0]) {
    const updated = await updateData.save(payload)
    if (updated) await policyData.reload()
  }

  return (
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

      <main className="mx-auto max-w-4xl px-6 py-6">
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

        <div className="rounded-card border border-border bg-surface p-5">
          <DocumentTypeEditor key={documentType} tenantId={tenantId} documentType={documentType} />
        </div>
      </main>
    </div>
  )
}
