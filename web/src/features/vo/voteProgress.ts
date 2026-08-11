import type { ApprovalActionDto, VariationOrderDto } from './types'

export interface StepVoteProgress {
  required: number
  satisfied: number
}

/**
 * Real vote progress ("X of Y signatures collected") for the VO's *current* step, computed from the
 * append-only approval-action history — mirroring the backend's own counting rule exactly
 * (`ApproveVariationOrderCommandHandler`/`RejectVariationOrderCommandHandler`: the count of
 * **distinct** `ActorUserId` who cast `action` on this `RevisionNo`+`StepNo`). `required` comes from
 * `VariationOrderDto.approvalSteps[currentStepNo].quorumCount`, which — unlike
 * `PaymentCertificateDto` — genuinely is on the wire (see `types.ts`'s remarks).
 *
 * Returns `null` when the count cannot be honestly derived — no chain attached, a corrupt/legacy
 * snapshot with no matching step, or (today, realistically always, since
 * `GetApprovalActionHistoryQueryHandler` has no `VariationOrder` existence-check arm yet — see
 * `api.ts#getVoApprovalActions`'s remarks) `history` itself unavailable — **never a guess**. Callers
 * must fall back to the coarser "your vote was recorded, more signatures needed" notice
 * (`useVariationOrderActions.ts`'s before/after status diff, the same approach Sprint 9 used for
 * Payment Certificate) when this returns `null`.
 */
export function computeStepVoteProgress(
  vo: VariationOrderDto,
  history: ApprovalActionDto[] | null,
  action: 'Approve' | 'Reject',
): StepVoteProgress | null {
  if (!history) return null
  if (vo.currentStepNo < 1 || vo.totalSteps < 1) return null

  const step = vo.approvalSteps.find((s) => s.stepNo === vo.currentStepNo)
  if (!step) return null

  const distinctVoters = new Set(
    history
      .filter((a) => a.revisionNo === vo.revisionNo && a.stepNo === vo.currentStepNo && a.action === action)
      .map((a) => a.actorUserId),
  )

  return { required: step.quorumCount, satisfied: distinctVoters.size }
}
