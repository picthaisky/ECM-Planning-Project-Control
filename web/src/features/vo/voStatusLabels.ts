import type { VariationOrderStatus, VariationOrderType } from './types'

/**
 * Thai display text for `VariationOrderStatus` (domain-rules.md §1/§2).
 *
 * **The trap this table exists to avoid** (S10-FE-01 DoD, stated explicitly): the working
 * prototype's own VO table (`docs/ECM Planning Prototype.dc.html` ~line 833, `voBadge`) maps a
 * *rejected* row's badge text to the Thai word **"ตีกลับ"** — but "ตีกลับ" is this product's name
 * for `ReturnForRevision` (a non-terminal refusal: the document returns to `Draft`, `RevisionNo`
 * bumps, and it **can be resubmitted**), not for `Rejected` (a terminal, unrecoverable refusal — a
 * replacement is a brand-new document with a new number). Sprint 9 hit the identical trap for
 * Payment Certificate and fixed it the same way: "ตีกลับ" appears **only** as the action button/
 * modal title for `ReturnForRevision` (`ApprovalActionModal.tsx`'s `return` kind), never as a status
 * label. `Rejected` gets its own, unambiguous label below ("ปฏิเสธ") instead.
 */
export const VO_STATUS_LABELS: Record<VariationOrderStatus, string> = {
  Draft: 'ฉบับร่าง',
  PendingApproval: 'รออนุมัติ',
  Approved: 'อนุมัติแล้ว',
  Rejected: 'ปฏิเสธ',
  Cancelled: 'ยกเลิก',
}

/** Thai display text for `VariationOrderType` (derived server-side from `sign(Amount)`) — matches
 * the prototype's own "งานเพิ่ม"/"งานลด" wording (`docs/ECM Planning Prototype.dc.html` ~line 838). */
export const VO_TYPE_LABELS: Record<VariationOrderType, string> = {
  Add: 'งานเพิ่ม',
  Deduct: 'งานลด',
}
