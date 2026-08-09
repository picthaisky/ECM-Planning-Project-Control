import type { ApprovalPolicyRule } from './types'

/**
 * Client-side mirror of the two Sprint 2/9 backend band checks, ported field-for-field from the
 * real source rather than re-derived from prose, so "no problems here" and "the server will accept
 * this" agree in the overwhelmingly common case (S9-FE-03 DoD: "ตรวจ band ทับซ้อน/ช่องว่างแบบ inline
 * ก่อนบันทึก"):
 *
 * 1. `UpdateApprovalPolicyCommandHandler.FindOverlappingStepNo` (Application layer, additive
 *    pre-check) — two rules that share a `StepNo` with intersecting `[MinAmount, MaxAmount)` ranges.
 * 2. `ApprovalPolicy.ValidateBands`/`AssertContiguousStepsAt` (Domain layer) — decomposes the full
 *    amount axis into atomic intervals at every rule boundary and requires the `StepNo`s covering
 *    any *claimed* interval to be exactly the contiguous sequence `{1,...,k}` (a gap = a skipped
 *    `StepNo`; an interval nobody claims at all is legal — approval-workflow.md §5.3 step 6 handles
 *    that at *routing* time via `ApprovalPolicyGap`, not here).
 *
 * These two checks are provably equivalent for *overlap* detection (sorting a `StepNo` group by
 * `MinAmount` and checking only adjacent pairs is a standard, complete interval-overlap test — if
 * any two ranges in the group intersect, some adjacent pair after sorting also intersects), so this
 * module runs #1 for overlaps (matching the backend's own `BandOverlap:{stepNo}` error shape
 * exactly) and #2 for gaps only (`BandGap:{stepNo}`).
 *
 * This is a **client-side convenience**, not the authority — the server re-validates independently
 * on every `PUT` (`UpdateApprovalPolicyCommandValidator` + `ApprovalPolicy`'s own constructor) and
 * always wins if the two ever disagree.
 */

export interface BandOverlapProblem {
  kind: 'overlap'
  stepNo: number
}

export interface BandGapProblem {
  kind: 'gap'
  stepNo: number
}

export type BandProblem = BandOverlapProblem | BandGapProblem

interface AmountBand {
  stepNo: number
  minAmount: number
  maxAmount: number | null
}

/** Mirrors `UpdateApprovalPolicyCommandHandler.FindOverlappingStepNo` exactly: within each `StepNo`
 * group (sorted by `MinAmount`), two adjacent bands intersect when the earlier one is unbounded or
 * its `MaxAmount` exceeds the next one's `MinAmount`. Returns every offending `StepNo`, not just the
 * first — a save-blocking form should surface every problem at once, not one round trip at a time. */
function findOverlappingStepNos(rules: AmountBand[]): number[] {
  const offending = new Set<number>()
  const byStepNo = new Map<number, AmountBand[]>()
  for (const rule of rules) {
    const group = byStepNo.get(rule.stepNo) ?? []
    group.push(rule)
    byStepNo.set(rule.stepNo, group)
  }

  for (const [stepNo, group] of byStepNo) {
    const ordered = [...group].sort((a, b) => a.minAmount - b.minAmount)
    for (let i = 0; i < ordered.length - 1; i++) {
      const currentMax = ordered[i].maxAmount
      const nextMin = ordered[i + 1].minAmount
      if (currentMax === null || currentMax > nextMin) {
        offending.add(stepNo)
      }
    }
  }

  return [...offending].sort((a, b) => a - b)
}

function collectBoundaries(rules: AmountBand[]): number[] {
  const points = new Set<number>()
  for (const rule of rules) {
    points.add(rule.minAmount)
    if (rule.maxAmount !== null) points.add(rule.maxAmount)
  }
  return [...points].sort((a, b) => a - b)
}

function coveringStepNos(rules: AmountBand[], lower: number, upper: number | null): number[] {
  const covering = rules.filter(
    (r) => r.minAmount <= lower && (upper === null ? r.maxAmount === null : r.maxAmount === null || r.maxAmount >= upper),
  )
  return [...new Set(covering.map((r) => r.stepNo))].sort((a, b) => a - b)
}

/** Mirrors `ApprovalPolicy.ValidateBands`/`AssertContiguousStepsAt`'s gap half exactly (the overlap
 * half is intentionally *not* duplicated here — see `findOverlappingStepNos` above for why it is
 * both sufficient and the one whose `StepNo` the backend's own error code reports). Returns every
 * distinct missing `StepNo` found across every atomic interval, deduplicated — the same missing
 * rung can span several consecutive intervals and should only be reported once. */
function findBandGaps(rules: AmountBand[]): number[] {
  if (rules.length === 0) return []

  const boundaries = collectBoundaries(rules)
  const missing = new Set<number>()

  function checkInterval(lower: number, upper: number | null) {
    const stepNumbers = coveringStepNos(rules, lower, upper)
    for (let expected = 1; expected <= stepNumbers.length; expected++) {
      if (stepNumbers[expected - 1] !== expected) {
        missing.add(expected)
        return // one interval reports at most one missing rung, same as the backend's own throw-per-interval
      }
    }
  }

  for (let i = 0; i < boundaries.length - 1; i++) {
    checkInterval(boundaries[i], boundaries[i + 1])
  }
  checkInterval(boundaries[boundaries.length - 1], null)

  return [...missing].sort((a, b) => a - b)
}

/** Validates the amount bands only (not per-field shape — see `validatePolicyDraft` for the full
 * picture). Callers pass already-numeric bands; `ApprovalPolicyRule.minAmount`/`maxAmount` are wire
 * strings and must be `Number(...)`-parsed by the caller first (never compared as strings). */
export function validateApprovalPolicyBands(rules: AmountBand[]): BandProblem[] {
  const overlaps = findOverlappingStepNos(rules).map((stepNo): BandOverlapProblem => ({ kind: 'overlap', stepNo }))
  const gaps = findBandGaps(rules).map((stepNo): BandGapProblem => ({ kind: 'gap', stepNo }))
  return [...overlaps, ...gaps].sort((a, b) => a.stepNo - b.stepNo)
}

/**
 * Client-side upper bound on `QuorumCount` (security review sprint-09.md finding N-02, Medium,
 * still open server-side as of this sprint).
 *
 * **This is a usability guard against a foot-gun, not a security control.** The engine now
 * genuinely enforces quorum (H-02 fix), which makes an over-large value *binding*: `ApprovePayment
 * CertificateCommandHandler`'s `DuplicateChainApprover` check caps the achievable quorum at the
 * number of *distinct users holding that role in the tenant* — so `QuorumCount = 3` on a role held
 * by only two people permanently strands every certificate routed to that step in `PendingApproval`,
 * with its money fields frozen, and there is no `Withdraw`/`Cancel` endpoint wired up anywhere to
 * recover it (`grep ".Withdraw(\|.Cancel(" backend/src` returns zero production call sites). The
 * server-side bound the review recommends (`UpdateApprovalPolicyCommandValidator`, e.g.
 * `LessThanOrEqualTo(5)`) is a tracked follow-up and is **not** in place yet —
 * `UpdateApprovalPolicyCommandValidator.cs:26` still only checks `GreaterThanOrEqualTo(1)`. A
 * request built outside this form (a hand-crafted `PUT`, or a future caller of this same API) can
 * still set any positive integer; this bound only stops an Admin from fat-fingering the number in
 * *this* UI.
 */
export const MAX_CLIENT_QUORUM_COUNT = 5

export interface RuleFieldIssues {
  ruleIndex: number
  stepNo?: string
  minAmount?: string
  maxAmount?: string
  requiredRole?: string
  quorumCount?: string
}

/** Mirrors `UpdateApprovalPolicyCommandValidator`'s per-rule checks exactly (`StepNo >= 1`,
 * `MinAmount >= 0`, `MaxAmount > MinAmount` when supplied, `QuorumCount >= 1`, `RequiredRole` a real
 * enum value) — plus the client-only `QuorumCount <= MAX_CLIENT_QUORUM_COUNT` foot-gun guard above. */
export function validateRuleFields(rules: ApprovalPolicyRule[]): RuleFieldIssues[] {
  const issues: RuleFieldIssues[] = []

  rules.forEach((rule, ruleIndex) => {
    const fieldIssue: RuleFieldIssues = { ruleIndex }
    const min = Number(rule.minAmount)
    const max = rule.maxAmount === null || rule.maxAmount.trim() === '' ? null : Number(rule.maxAmount)

    if (!Number.isInteger(rule.stepNo) || rule.stepNo < 1) {
      fieldIssue.stepNo = 'StepNo ต้องเป็นจำนวนเต็มตั้งแต่ 1 ขึ้นไป'
    }
    if (rule.minAmount.trim() === '' || Number.isNaN(min) || min < 0) {
      fieldIssue.minAmount = 'จำนวนเงินขั้นต่ำต้องไม่ติดลบ'
    }
    if (max !== null && (Number.isNaN(max) || max <= min)) {
      fieldIssue.maxAmount = 'จำนวนเงินสูงสุดต้องมากกว่าขั้นต่ำ (หรือเว้นว่างไว้สำหรับไม่จำกัด)'
    }
    if (!rule.requiredRole) {
      fieldIssue.requiredRole = 'กรุณาเลือกบทบาทผู้อนุมัติ'
    }
    if (!Number.isInteger(rule.quorumCount) || rule.quorumCount < 1) {
      fieldIssue.quorumCount = 'Quorum ต้องเป็นจำนวนเต็มตั้งแต่ 1 ขึ้นไป'
    } else if (rule.quorumCount > MAX_CLIENT_QUORUM_COUNT) {
      fieldIssue.quorumCount = `Quorum สูงสุด ${MAX_CLIENT_QUORUM_COUNT} คนในหน้านี้ (ป้องกันการตั้งค่าที่ทำให้เอกสารติดค้างถาวร — ดูคำอธิบายด้านล่าง)`
    }

    if (Object.keys(fieldIssue).length > 1) issues.push(fieldIssue)
  })

  return issues
}

export interface PolicyDraftValidation {
  fieldIssues: RuleFieldIssues[]
  bandProblems: BandProblem[]
  /** Mirrors `UpdateApprovalPolicyCommandValidator.RuleFor(x => x.Rules).NotEmpty()`. */
  hasNoRules: boolean
  isValid: boolean
}

/** The full inline pre-save check the S9-FE-03 editor runs on every render of the draft rule set. */
export function validatePolicyDraft(rules: ApprovalPolicyRule[]): PolicyDraftValidation {
  const fieldIssues = validateRuleFields(rules)
  const hasNoRules = rules.length === 0

  // Band validation assumes every field is individually well-formed — skip it while fields are
  // broken so the user sees one coherent problem at a time instead of noisy, derived nonsense
  // (e.g. `Number('')` -> `NaN` poisoning every interval boundary comparison).
  const bandProblems =
    hasNoRules || fieldIssues.length > 0
      ? []
      : validateApprovalPolicyBands(
          rules.map((r) => ({
            stepNo: r.stepNo,
            minAmount: Number(r.minAmount),
            maxAmount: r.maxAmount === null || r.maxAmount.trim() === '' ? null : Number(r.maxAmount),
          })),
        )

  return {
    fieldIssues,
    bandProblems,
    hasNoRules,
    isValid: !hasNoRules && fieldIssues.length === 0 && bandProblems.length === 0,
  }
}
