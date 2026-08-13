import { useState } from 'react'
import { useParams } from 'react-router-dom'
import { Button } from '../../components'
import { useAuthStore } from '../../store/authStore'
import type { UserRole } from '../../store/authStore'
import { canAttemptApprove, canAttemptReject, canAttemptReturnForRevision, resolveChainStepTone } from './chainPermissions'
import { ApprovalChainBar } from './components/ApprovalChainBar'
import { CertificatePanel } from './components/CertificatePanel'
import { CreateCertificateModal } from './components/CreateCertificateModal'
import { MilestoneCertificateTable } from './components/MilestoneCertificateTable'
import { ProjectSettingsModal } from './components/ProjectSettingsModal'
import { PAYMENT_STATUS_LABELS } from './paymentStatusLabels'
import { useApprovalActions } from './useApprovalActions'
import { usePaymentCertificateActions } from './usePaymentCertificateActions'
import { usePaymentCertificates } from './usePaymentCertificates'
import { useProjectMasterData } from '../info'

/** Roles allowed to submit a Draft certificate for approval — mirrors
 * `PaymentCertificatesController.CertificateCrudRoles` exactly (`QS,PM,ProjectDirector,Admin`).
 * UX affordance only; the server re-checks independently. */
const SUBMIT_ROLES: readonly UserRole[] = ['QS', 'PM', 'ProjectDirector', 'Admin']

/**
 * S9-FE-01/02 "Payment Certificate" screen (US-9.2/9.3): the milestone list (left, prototype screen
 * #7) + the certificate detail panel with the fixed Gross → Retention → Advance recovery → Net
 * breakdown (right, sticky) + the S9-FE-02 approval-chain bar beneath it.
 *
 * The milestone list, the single-certificate reload, and now certificate creation are all real, live
 * backend endpoints (the S9 read-side L-04 gap and the S9-BE-05 create gap are both closed). The
 * "สร้างใบรับรอง" action opens {@link CreateCertificateModal}; the server computes every money field
 * and the created certificate is upserted + selected via `applyUpdatedCertificate` (no list refetch).
 */
export function PaymentPage() {
  const { projectId } = useParams<{ projectId: string }>()
  const currentUserId = useAuthStore((state) => state.claims?.userId ?? null)
  const currentUserRole = useAuthStore((state) => state.claims?.role ?? null)
  const [isSettingsOpen, setIsSettingsOpen] = useState(false)
  const [isCreateOpen, setIsCreateOpen] = useState(false)

  const certificates = usePaymentCertificates(projectId ?? '')
  const history = useApprovalActions(certificates.selected?.id ?? null)
  const actions = usePaymentCertificateActions(certificates.selected, certificates.applyUpdatedCertificate)
  const projectData = useProjectMasterData(projectId ?? '')

  if (!projectId) return null

  const canSubmit = currentUserRole !== null && SUBMIT_ROLES.includes(currentUserRole)
  // Create shares CertificateCrudRoles with submit server-side (ProjectPaymentCertificatesController).
  const canCreate = canSubmit

  return (
    <div className="grid grid-cols-1 items-start gap-4 lg:grid-cols-[1.5fr_1fr]">
      <div className="flex flex-col gap-4">
        {canCreate && (
          <div className="flex justify-end">
            <Button size="sm" onClick={() => setIsCreateOpen(true)}>
              + สร้างใบรับรองผลงาน
            </Button>
          </div>
        )}

        <MilestoneCertificateTable
          certificates={certificates.certificates}
          selectedId={certificates.selectedId}
          onSelect={certificates.select}
          state={certificates.loadState}
          errorMessage={certificates.loadError ?? undefined}
        />

        {certificates.selected && (
          <ApprovalChainBar
            statusLabel={PAYMENT_STATUS_LABELS[certificates.selected.status]}
            statusValue={certificates.selected.status}
            totalSteps={certificates.selected.totalSteps}
            currentStepNo={certificates.selected.currentStepNo}
            notSubmittedYet={certificates.selected.status === 'NotDue' || certificates.selected.status === 'Draft'}
            stepTone={(stepNo) => resolveChainStepTone(certificates.selected!, stepNo)}
            history={history.actions}
            historyState={history.state}
            historyUnavailableReason={history.unavailableReason}
            quorumPendingNotice={actions.quorumPendingNotice}
            canApprove={canAttemptApprove(certificates.selected, currentUserId)}
            canReturnForRevision={canAttemptReturnForRevision(certificates.selected)}
            canReject={canAttemptReject(certificates.selected)}
            busy={actions.busyAction !== null}
            actionError={actions.actionError}
            onApprove={actions.approve}
            onReturnForRevision={actions.returnForRevision}
            onReject={actions.reject}
          />
        )}
      </div>

      <CertificatePanel
        certificate={certificates.selected}
        project={projectData.project}
        onOpenSettings={() => setIsSettingsOpen(true)}
        onSubmit={() => void actions.submit()}
        submitting={actions.busyAction === 'submit'}
        canSubmit={canSubmit}
      />

      <CreateCertificateModal
        projectId={projectId}
        isOpen={isCreateOpen}
        onClose={() => setIsCreateOpen(false)}
        onCreated={certificates.applyUpdatedCertificate}
      />

      <ProjectSettingsModal
        isOpen={isSettingsOpen}
        onClose={() => setIsSettingsOpen(false)}
        project={projectData.project}
        saving={projectData.saveState === 'saving'}
        serverError={projectData.saveError}
        onSubmit={(payload) => {
          void projectData.save(payload).then((ok) => {
            if (ok) setIsSettingsOpen(false)
          })
        }}
      />
    </div>
  )
}
