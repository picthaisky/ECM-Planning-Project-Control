import { ROLE_LABEL } from '../../utils/roleLabels'
import { formatMoney, formatPercent } from '../../utils/format'
import type { VariationOrderDto } from './types'

/**
 * S10-FE-01 DoD: "แสดง chain ที่ resolve แล้วตอน submit พร้อมเหตุผลเมื่อมี escalation" — when an
 * escalation step is present, explain *why* rather than showing an unexplained extra approver.
 *
 * **What is honestly knowable, and what is not.** `VariationOrderDto.approvalSteps` carries the
 * snapshotted chain (role/quorum per step) but — confirmed from source, not assumed — carries no
 * flag distinguishing "this step exists because of the cumulative-VO-escalation rule (domain-
 * rules.md §4)" from "this step exists because the VO's own amount fell in a high-value band that
 * already includes this role" (`ApprovalRoutingService.Resolve`'s `EscalationApplied` bit is
 * computed at Submit time but never persisted onto the aggregate or projected onto the DTO). Nor is
 * the pinned policy's threshold/baseline fetchable by this screen's own actors (PM/Planning/QS/
 * ProjectDirector) — `GET /api/v1/tenants/{id}/approval-policies` is Admin-only, and
 * `Project.EscalationBaselineContractValue` is not on any `ProjectDto` a non-Admin role can read.
 *
 * Given that, this function never *asserts* a step is escalation-driven. It surfaces the one
 * structural signal that is honestly derivable — the chain's final role — together with whatever
 * real cumulative-VO context is available, worded to distinguish a stated fact ("the final step
 * requires Executive") from a hedged inference ("may relate to cumulative VO exceeding a configured
 * threshold"). For an already-`Approved` VO the exact figure the backend actually used
 * (`cumulativeVoPctAtApproval`) **is** on the wire (written once at approval, domain-rules.md §2.4)
 * and is stated as fact, not hedged.
 *
 * Returns `null` when there is nothing worth surfacing (no chain, or the final role is not the kind
 * that plausibly signals escalation) — `ApprovalChainBar` renders no box at all in that case, never
 * an empty one.
 */
export function buildEscalationNote(
  vo: VariationOrderDto,
  cumulativeApprovedVoBefore: number | null,
): string | null {
  if (vo.totalSteps === 0 || vo.approvalSteps.length === 0) return null

  const finalStep = vo.approvalSteps[vo.approvalSteps.length - 1]
  // Executive is, by every seeded/example policy in the domain spec, the highest tier and the only
  // role the cumulative-VO-escalation rule ever appends (`ApprovalPolicy.CumulativeVoEscalationRole`
  // — nullable per-policy, but Executive in every fixture) — the one structural signal worth
  // surfacing at all. Any other final role is ordinary routing, not worth a note.
  if (finalStep.requiredRole !== 'Executive') return null

  const roleLabel = ROLE_LABEL.Executive

  if (vo.status === 'Approved' && vo.cumulativeVoPctAtApproval !== null) {
    return `ขั้นตอนอนุมัติสุดท้ายกำหนดให้ ${roleLabel} อนุมัติ — มูลค่า VO สะสมของโครงการ ณ วันที่อนุมัติเท่ากับ ${formatPercent(vo.cumulativeVoPctAtApproval)} ของมูลค่าสัญญาฐาน`
  }

  if (cumulativeApprovedVoBefore !== null) {
    const cumulativeAfterThisVo = cumulativeApprovedVoBefore + Number(vo.amount)
    return `ขั้นตอนอนุมัติสุดท้ายกำหนดให้ ${roleLabel} อนุมัติ — อาจเกี่ยวข้องกับมูลค่า VO สะสมของโครงการ (รวม VO นี้ ประมาณ ${formatMoney(cumulativeAfterThisVo)} บาท) ที่เข้าเกณฑ์ escalation ของนโยบายที่ตั้งไว้ (ตัวเลข % ที่แน่นอนจะแสดงหลังอนุมัติสำเร็จ)`
  }

  return `ขั้นตอนอนุมัติสุดท้ายกำหนดให้ ${roleLabel} อนุมัติ`
}
