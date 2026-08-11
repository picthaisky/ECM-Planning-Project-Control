import { useMemo, useState } from 'react'
import { useParams } from 'react-router-dom'
import { Button } from '../../components'
import { useAuthStore } from '../../store/authStore'
import type { UserRole } from '../../store/authStore'
import { ApprovalChainBar } from '../payment/components/ApprovalChainBar'
import {
  canAttemptApprove,
  canAttemptCancel,
  canAttemptEditContent,
  canAttemptReject,
  canAttemptReturnForRevision,
  canAttemptSubmit,
  canAttemptWithdraw,
  resolveChainStepTone,
} from './chainPermissions'
import { CancelVoModal } from './components/CancelVoModal'
import { VoContentModal } from './components/VoContentModal'
import { VoDetailPanel } from './components/VoDetailPanel'
import { VoSummaryTiles } from './components/VoSummaryTiles'
import { VoTable } from './components/VoTable'
import { buildEscalationNote } from './escalationNote'
import { useVariationOrderActions } from './useVariationOrderActions'
import { useVariationOrders } from './useVariationOrders'
import { useVoApprovalActions } from './useVoApprovalActions'
import { useVoContentSubmit } from './useVoContentSubmit'
import { computeStepVoteProgress } from './voteProgress'
import { VO_STATUS_LABELS } from './voStatusLabels'
import { useProjectMasterData } from '../info'

/** Roles allowed to create/edit a VO — mirrors `ProjectVariationOrdersController`/
 * `VariationOrdersController.VoCrudRoles` exactly (`PM,Planning,QS,ProjectDirector,Admin`). UX
 * affordance only; the server re-checks independently. Notably **not** `Executive` — see
 * `api.ts#getVariationOrder`'s remarks on the real authorization gap this leaves for an Executive
 * approver added by escalation (they can call `approve` directly, but cannot browse this screen). */
const VO_CRUD_ROLES: readonly UserRole[] = ['PM', 'Planning', 'QS', 'ProjectDirector', 'Admin']

/**
 * S10-FE-01 "Variation Order" screen (US-10.2): the VO register (left, prototype screen #9) +
 * detail panel (right, sticky, mirrors `features/payment/PaymentPage.tsx`'s established master-
 * detail split) + the reused `ApprovalChainBar` (S9-FE-02 component, per the DoD's explicit
 * instruction) beneath the list.
 *
 * Two requirements the DoD calls out explicitly, both closed:
 * - The "ตีกลับ" badge never appears for `Rejected` — `voStatusLabels.ts`'s own remarks document the
 *   exact prototype trap this avoids; `ApprovalChainBar`'s "ตีกลับแก้ไข" button is the only place
 *   that Thai word appears, and it maps to `ReturnForRevision` (non-terminal, resubmittable — the
 *   "แก้ไข" button plus `VoContentModal` is exactly that resubmission path).
 * - The resolved chain is shown as snapshotted at submit (`vo.approvalSteps`/`totalSteps`/
 *   `currentStepNo`, all real DTO fields), with an escalation explanation when the final step is
 *   `Executive` (`escalationNote.ts` — hedged when the exact backend numbers are not derivable,
 *   stated as fact when they are).
 */
export function VoPage() {
  const { projectId } = useParams<{ projectId: string }>()
  const currentUserId = useAuthStore((state) => state.claims?.userId ?? null)
  const currentUserRole = useAuthStore((state) => state.claims?.role ?? null)

  const vos = useVariationOrders(projectId ?? '')
  const history = useVoApprovalActions(vos.selected?.id ?? null)
  const actions = useVariationOrderActions(vos.selected, vos.applyUpdatedVo)
  const contentSubmit = useVoContentSubmit(projectId ?? '', vos.applyUpdatedVo)
  const projectData = useProjectMasterData(projectId ?? '')

  const [createModalOpen, setCreateModalOpen] = useState(false)
  const [editModalOpen, setEditModalOpen] = useState(false)
  const [cancelModalOpen, setCancelModalOpen] = useState(false)

  const currentBac = projectData.loadState === 'ready' && projectData.project ? Number(projectData.project.bac) : null
  const currentContractValue =
    projectData.loadState === 'ready' && projectData.project ? Number(projectData.project.contractValue) : null

  // ADR-0015 N-1 (net-signed): the running total of this project's already-Approved VOs, excluding
  // the currently-selected one — always derivable client-side from the real, live VO list (unlike
  // the baseline contract value / threshold, neither of which any endpoint this screen's roles can
  // call exposes — see `bacImpact.ts`'s own remarks). `null` only while the list itself has not
  // loaded successfully yet, never a guessed partial sum.
  const cumulativeApprovedVoBefore = useMemo(() => {
    if (vos.loadState !== 'ready') return null
    return vos.variationOrders
      .filter((v) => v.status === 'Approved' && v.id !== vos.selected?.id)
      .reduce((sum, v) => sum + Number(v.amount), 0)
  }, [vos.loadState, vos.variationOrders, vos.selected?.id])

  // Prefer the REAL vote count (`voteProgress.ts`, derived from the append-only history) over the
  // generic before/after-diff notice the moment history is actually available — today that is
  // realistically never (`api.ts#getVoApprovalActions`'s remarks: the read 404s unconditionally
  // until a one-line backend gap is fixed), so this degrades to the hook's own generic message, but
  // it is wired up now rather than left dead, exactly like `ApprovalChainBar`'s own history section
  // already does for the identical reason.
  const quorumProgress = useMemo(() => {
    if (!actions.quorumPendingNotice || !vos.selected || !actions.quorumPendingAction) return null
    return computeStepVoteProgress(
      vos.selected,
      history.actions,
      actions.quorumPendingAction === 'reject' ? 'Reject' : 'Approve',
    )
  }, [actions.quorumPendingNotice, actions.quorumPendingAction, vos.selected, history.actions])

  const resolvedQuorumMessage = useMemo(() => {
    if (!quorumProgress || !vos.selected) return actions.quorumPendingMessage ?? undefined
    return actions.quorumPendingAction === 'reject'
      ? `บันทึกการปฏิเสธของคุณแล้ว (${quorumProgress.satisfied}/${quorumProgress.required} เสียง) แต่เอกสารฉบับนี้ยังไม่ถูกปฏิเสธ — ยังคงเป็น "รออนุมัติ"`
      : `บันทึกการอนุมัติของคุณแล้ว (${quorumProgress.satisfied}/${quorumProgress.required} เสียง) ในขั้นตอนที่ ${vos.selected.currentStepNo} — ยังต้องการผู้อนุมัติเพิ่มเติมให้ครบ Quorum`
  }, [quorumProgress, actions.quorumPendingAction, actions.quorumPendingMessage, vos.selected])

  if (!projectId) return null

  const canCreate = currentUserRole !== null && VO_CRUD_ROLES.includes(currentUserRole)
  const selected = vos.selected

  return (
    <div className="flex flex-col gap-4">
      <VoSummaryTiles
        variationOrders={vos.variationOrders}
        currentBac={currentBac}
        bacLoadState={projectData.loadState === 'loading' ? 'loading' : projectData.loadState === 'error' ? 'error' : 'ready'}
      />

      <div className="grid grid-cols-1 items-start gap-4 lg:grid-cols-[1.5fr_1fr]">
        <div className="flex flex-col gap-4">
          <div className="flex justify-end">
            {canCreate && (
              <Button size="sm" onClick={() => setCreateModalOpen(true)}>
                + สร้าง Variation Order
              </Button>
            )}
          </div>

          <VoTable
            variationOrders={vos.variationOrders}
            selectedId={vos.selectedId}
            onSelect={vos.select}
            state={vos.loadState}
            errorMessage={vos.loadError ?? undefined}
          />

          {selected && (
            <ApprovalChainBar
              title="เส้นทางการอนุมัติ VO (Approval Chain)"
              statusLabel={VO_STATUS_LABELS[selected.status]}
              statusValue={selected.status}
              totalSteps={selected.totalSteps}
              currentStepNo={selected.currentStepNo}
              notSubmittedYet={selected.status === 'Draft'}
              notSubmittedMessage="ยังไม่ได้ส่งขออนุมัติ Variation Order ฉบับนี้"
              stepTone={(stepNo) => resolveChainStepTone(selected, stepNo)}
              history={history.actions}
              historyState={history.state}
              historyUnavailableReason={history.unavailableReason}
              escalationNote={buildEscalationNote(selected, cumulativeApprovedVoBefore)}
              quorumPendingNotice={actions.quorumPendingNotice}
              quorumPendingMessage={resolvedQuorumMessage}
              canApprove={canAttemptApprove(selected, currentUserId)}
              canReturnForRevision={canAttemptReturnForRevision(selected)}
              canReject={canAttemptReject(selected)}
              busy={actions.busyAction !== null}
              actionError={actions.actionError}
              onApprove={actions.approve}
              onReturnForRevision={actions.returnForRevision}
              onReject={actions.reject}
            />
          )}
        </div>

        <VoDetailPanel
          vo={selected}
          currentBac={currentBac}
          currentContractValue={currentContractValue}
          cumulativeApprovedVoBefore={cumulativeApprovedVoBefore}
          escalationBaselineContractValue={null}
          escalationThresholdPct={null}
          onOpenEdit={() => setEditModalOpen(true)}
          canEdit={selected ? canAttemptEditContent(selected) && canCreate : false}
          onSubmit={() => void actions.submit()}
          submitting={actions.busyAction === 'submit'}
          canSubmit={selected ? canAttemptSubmit(selected) && canCreate : false}
          onWithdraw={() => void actions.withdraw()}
          withdrawing={actions.busyAction === 'withdraw'}
          canWithdraw={selected ? canAttemptWithdraw(selected, currentUserId) : false}
          onOpenCancel={() => setCancelModalOpen(true)}
          canCancel={selected ? canAttemptCancel(selected, currentUserId, currentUserRole) : false}
        />
      </div>

      {createModalOpen && (
        <VoContentModal
          mode="create"
          isOpen
          onClose={() => {
            contentSubmit.clearError()
            setCreateModalOpen(false)
          }}
          busy={contentSubmit.busy}
          errorMessage={contentSubmit.error}
          onCreate={(payload) => {
            void contentSubmit.create(payload).then((created) => {
              if (created) setCreateModalOpen(false)
            })
          }}
          onUpdate={() => {}}
        />
      )}

      {editModalOpen && selected && (
        <VoContentModal
          mode="edit"
          isOpen
          initialVo={selected}
          onClose={() => {
            contentSubmit.clearError()
            setEditModalOpen(false)
          }}
          busy={contentSubmit.busy}
          errorMessage={contentSubmit.error}
          onCreate={() => {}}
          onUpdate={(id, payload) => {
            void contentSubmit.update(id, payload).then((updated) => {
              if (updated) setEditModalOpen(false)
            })
          }}
        />
      )}

      {cancelModalOpen && selected && (
        <CancelVoModal
          isOpen
          voNumber={selected.voNumber}
          onCancelModal={() => {
            actions.clearActionError()
            setCancelModalOpen(false)
          }}
          busy={actions.busyAction === 'cancel'}
          errorMessage={actions.actionError}
          onConfirm={(comment) => {
            void actions.cancel(comment).then((result) => {
              if (result) setCancelModalOpen(false)
            })
          }}
        />
      )}
    </div>
  )
}
