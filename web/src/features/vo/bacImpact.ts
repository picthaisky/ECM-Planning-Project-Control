/**
 * S10-FE-02 core: the BAC/ContractValue impact math, kept as a pure module (no fetching, no React)
 * so it can be unit-tested directly against the domain spec's own fixtures — V-9 (the BAC-move
 * arithmetic) and V-5a/V-5b (the escalation-threshold boundary) — without mocking anything.
 * `components/BacImpactPanel.tsx` renders this module's output; it never recomputes the formulas
 * itself (CLAUDE.md: never diverge from the backend's own math).
 *
 * Mirrors two backend sources exactly:
 * - `Project.ApplyVariationOrderApproval` (domain-rules.md §5.1): `BAC_new = BAC_old + A`,
 *   `ContractValue_new = ContractValue_old + A`, **both signed** — a `Deduct` VO (negative `A`)
 *   lowers both. Never `Math.abs()` — see this module's own remarks on `amount`.
 * - `ApprovalRoutingService.Resolve`'s escalation test (domain-rules.md §4.1/§4.6, ADR-0015):
 *   $\Phi = (\Sigma^{VO}_{prior} + A) / C^{esc} \times 100$, escalates iff $\theta \ne \text{NULL}$
 *   and $\Phi > \theta$ — **strict** `>`, compared at full precision (fixture V-5b exists
 *   specifically to catch an implementation that rounds $\Phi$ to `decimal(5,2)` before comparing).
 */

export interface BacImpactInput {
  /** `Project.BAC` *before* this VO's effect — the live current value. */
  bacBefore: number
  /** `Project.ContractValue` *before* this VO's effect. */
  contractValueBefore: number
  /** $A$ — the VO's own signed amount. `Add` ⟹ positive, `Deduct` ⟹ negative. Passed through as-is
   * everywhere in this module; never `Math.abs()`'d — the backend deliberately preserves the sign
   * (domain-rules.md §5.1), and collapsing it here would misrepresent a Deduct VO as an increase. */
  amount: number
  /** $\Sigma^{VO}_{prior}$ — net-signed (additions less deductions, ADR-0015 N-1) total of this
   * project's already-`Approved` VOs, excluding this one. `null` when the caller could not derive
   * it (e.g. the VO list failed to load) — kept distinct from `0` so "no prior VOs" and "unknown"
   * are never confused (mirrors `actual-cost.md`'s `ActualCostResult` `EntryCount` reasoning). */
  cumulativeApprovedVoBefore: number | null
  /** $C^{esc}$ — `Project.EscalationBaselineContractValue` (`OriginalContractValue ?? ContractValue`,
   * ADR-0015). `null` when the caller has no way to know it — see `BacImpactPanel.tsx`'s remarks on
   * why this is realistically always `null` today (no live endpoint exposes it to a non-Admin
   * role). */
  escalationBaselineContractValue: number | null
  /** $\theta$ — `ApprovalPolicy.CumulativeVoEscalationPct`. **`null` means "no escalation
   * configured"** (ADR-0015) and must never be treated as `0` — a `0` threshold would (wrongly)
   * imply every VO escalates. */
  escalationThresholdPct: number | null
  /** `ApprovalPolicy.CumulativeVoEscalationRole` — carried through only for display ("who the extra
   * approver would be"), never used in the escalate/no-escalate decision itself. */
  escalationRole?: string | null
}

export type EscalationAssessment =
  | { status: 'not-configured' }
  | { status: 'unknown' }
  | { status: 'below-threshold'; cumulativeAmount: number; pct: number; thresholdPct: number; role: string | null }
  | { status: 'crosses-threshold'; cumulativeAmount: number; pct: number; thresholdPct: number; role: string | null }

export interface BacImpactResult {
  bacBefore: number
  bacAfter: number
  /** Always equal to `amount` (signed) — kept as its own field so callers never need to re-derive
   * "before minus after" and risk flipping the sign. */
  bacDelta: number
  contractValueBefore: number
  contractValueAfter: number
  contractValueDelta: number
  escalation: EscalationAssessment
}

/**
 * Decides escalate/no-escalate via cross-multiplication (`cumulativeAmount * 100 > thresholdPct *
 * baseline`) rather than `(cumulativeAmount / baseline) * 100 > thresholdPct`. Both sides stay well
 * within `Number.MAX_SAFE_INTEGER` for realistic contract values (low billions of baht at 2dp), so
 * this is exact — it removes the one intermediate floating-point division step that could otherwise
 * nudge an exact-boundary ratio (fixture V-5a: $\Phi$ = exactly 10.000000%) a few ULPs to either
 * side of the threshold before the comparison even runs. The percentage shown to the user
 * (`pct` on the returned assessment) is still computed the "readable" way — division then ×100 —
 * since that value is for *display* only (rounded at the render boundary by `formatPercent`,
 * `utils/format.ts`) and never feeds back into a decision.
 */
function exceedsThreshold(cumulativeAmount: number, baselineContractValue: number, thresholdPct: number): boolean {
  return cumulativeAmount * 100 > thresholdPct * baselineContractValue
}

function computeEscalation(input: BacImpactInput): EscalationAssessment {
  const role = input.escalationRole ?? null

  if (input.escalationThresholdPct === null) {
    // ADR-0015 / domain-rules.md §4.5: NULL means "no escalation configured", never "0". Every
    // consumer must treat this as "no escalation step will be added", not render it as a 0%
    // threshold that would (wrongly) imply everything escalates.
    return { status: 'not-configured' }
  }

  if (
    input.cumulativeApprovedVoBefore === null ||
    input.escalationBaselineContractValue === null ||
    input.escalationBaselineContractValue <= 0
  ) {
    // domain-rules.md §4.6: a configured threshold with no usable baseline is a data-quality
    // problem the backend fails closed on (422 ContractValueNotConfigured) — the frontend has the
    // symmetric obligation to say "cannot tell" rather than silently rendering 0%/"no escalation".
    return { status: 'unknown' }
  }

  const cumulativeAmount = input.cumulativeApprovedVoBefore + input.amount
  const pct = (cumulativeAmount / input.escalationBaselineContractValue) * 100
  const thresholdPct = input.escalationThresholdPct

  return exceedsThreshold(cumulativeAmount, input.escalationBaselineContractValue, thresholdPct)
    ? { status: 'crosses-threshold', cumulativeAmount, pct, thresholdPct, role }
    : { status: 'below-threshold', cumulativeAmount, pct, thresholdPct, role }
}

/** The whole S10-FE-02 computation: BAC/ContractValue before → after (signed) plus the escalation
 * assessment. Pure — same input always yields the same output, no I/O. */
export function computeBacImpact(input: BacImpactInput): BacImpactResult {
  return {
    bacBefore: input.bacBefore,
    bacAfter: input.bacBefore + input.amount,
    bacDelta: input.amount,
    contractValueBefore: input.contractValueBefore,
    contractValueAfter: input.contractValueBefore + input.amount,
    contractValueDelta: input.amount,
    escalation: computeEscalation(input),
  }
}
