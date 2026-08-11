import { computeBacImpact } from '../bacImpact'
import { ROLE_LABEL } from '../../../utils/roleLabels'
import { formatMoney, formatPercent } from '../../../utils/format'
import type { UserRole } from '../../../store/authStore'

export interface BacImpactPanelProps {
  /** $A$ — the VO's own signed amount. Never `Math.abs()`'d anywhere in this component; a `Deduct`
   * VO's negative sign must reach the screen exactly as the backend carries it
   * (domain-rules.md §5.1). */
  amount: number
  /** `Project.BAC` *before* this VO. */
  bacBefore: number
  /** `Project.ContractValue` *before* this VO. */
  contractValueBefore: number
  /** $\Sigma^{VO}_{prior}$, net-signed, excluding this VO. `null` when unknown to the caller —
   * rendered as an honest "cannot compute" note, never a silent `0`. */
  cumulativeApprovedVoBefore: number | null
  /** $C^{esc}$, ADR-0015's baseline contract value. `null` when unknown to the caller. */
  escalationBaselineContractValue: number | null
  /** $\theta$ — **`null` means "no escalation configured"**, never `0` (ADR-0015). */
  escalationThresholdPct: number | null
  escalationRole?: UserRole | null
  /** Heading override — defaults to a submit/approve-time preview wording. Pass a different one
   * (e.g. "ผลกระทบที่บันทึกไว้ตอนอนุมัติ") when rendering the *actual* recorded figures of an
   * already-`Approved` VO instead of a live preview — see `VoDetailPanel.tsx`'s two call sites. */
  title?: string
}

function formatSigned(value: number): string {
  if (value > 0) return `+${formatMoney(value)}`
  return formatMoney(value) // Intl already renders the '-' for negative values; 0 needs no sign.
}

/**
 * S10-FE-02: the BAC/ContractValue impact panel ("แผงผลกระทบ BAC/ContractValue ก่อนยืนยัน", the DoD's
 * own wording). Shows BAC and ContractValue before → after and the project's cumulative-VO
 * percentage, and warns when *this* VO would cross the configured escalation threshold.
 *
 * Pure presentation over `bacImpact.ts`'s pure math — this component never recomputes a formula
 * itself, it only formats `computeBacImpact`'s output (CLAUDE.md: never diverge from the backend's
 * own math). Two things the task singled out as easy to get wrong, both handled in `bacImpact.ts`
 * and simply rendered honestly here:
 *
 * 1. **The amount is signed.** `formatSigned`/`formatMoney` never call `Math.abs()` anywhere in this
 *    file — a `Deduct` VO's negative amount, and the resulting *drop* in BAC/ContractValue, render
 *    with their true minus sign throughout, never re-cast as an increase.
 * 2. **The threshold is nullable, and `NULL` ≠ `0`.** When `escalationThresholdPct` is `null` (no
 *    escalation configured on the pinned policy), this panel says exactly that — never "0.00%",
 *    which would misleadingly imply every VO escalates.
 *
 * A third, undocumented-in-the-DoD-but-real constraint: `cumulativeApprovedVoBefore` and
 * `escalationBaselineContractValue` are **not derivable from any endpoint this screen's own actors
 * (PM/Planning/QS/ProjectDirector) can call** on the real backend today — `Project.
 * OriginalContractValue`/`EscalationBaselineContractValue` is not on any `ProjectDto` a non-Admin
 * role can read, and the one endpoint that carries the escalation threshold/role
 * (`GET /api/v1/tenants/{id}/approval-policies`) is `[Authorize(Roles = "Admin")]`-gated. When the
 * caller genuinely cannot supply these (pass `null`), this panel says "ไม่สามารถคำนวณได้" rather than
 * guessing a number — never a fabricated 0%/"no escalation" reading dressed up as a real one.
 */
export function BacImpactPanel({
  amount,
  bacBefore,
  contractValueBefore,
  cumulativeApprovedVoBefore,
  escalationBaselineContractValue,
  escalationThresholdPct,
  escalationRole,
  title = 'ผลกระทบต่อ BAC / มูลค่าสัญญา',
}: BacImpactPanelProps) {
  const impact = computeBacImpact({
    amount,
    bacBefore,
    contractValueBefore,
    cumulativeApprovedVoBefore,
    escalationBaselineContractValue,
    escalationThresholdPct,
    escalationRole,
  })
  const { escalation } = impact

  return (
    <div className="rounded-card border border-border bg-surface p-4">
      <h3 className="font-heading text-sm font-semibold text-navy">{title}</h3>

      <dl className="mt-3 flex flex-col divide-y divide-border-subtle text-[12.5px]">
        <div className="flex items-center justify-between gap-3 py-2">
          <dt className="text-text-faint">มูลค่า VO นี้ (Amount)</dt>
          <dd className={amount < 0 ? 'font-semibold text-danger' : 'font-semibold text-secondary'}>
            {formatSigned(amount)} บาท
          </dd>
        </div>

        <div className="flex items-center justify-between gap-3 py-2">
          <dt className="text-text-faint">BAC เดิม → ใหม่</dt>
          <dd className="font-semibold text-navy">
            {formatMoney(impact.bacBefore)} → {formatMoney(impact.bacAfter)} บาท
          </dd>
        </div>

        <div className="flex items-center justify-between gap-3 py-2">
          <dt className="text-text-faint">มูลค่าสัญญาเดิม → ใหม่ (ContractValue)</dt>
          <dd className="font-semibold text-navy">
            {formatMoney(impact.contractValueBefore)} → {formatMoney(impact.contractValueAfter)} บาท
          </dd>
        </div>

        <div className="py-2">
          <dt className="text-text-faint">% VO สะสมของโครงการ</dt>
          <dd className="mt-1">
            {escalation.status === 'not-configured' && (
              <p className="text-[11px] text-text-faint">
                โครงการนี้ไม่ได้ตั้งค่าเกณฑ์ escalation ของ VO สะสม (ไม่มีขั้นตอนอนุมัติเพิ่มเติมจากเกณฑ์นี้)
              </p>
            )}
            {escalation.status === 'unknown' && (
              <p className="rounded border border-dashed border-border px-3 py-2 text-[11px] text-text-faint">
                ยังไม่สามารถคำนวณ % VO สะสมของโครงการได้ในขณะนี้ (ไม่มีข้อมูลมูลค่าสัญญาฐานของโครงการ)
              </p>
            )}
            {(escalation.status === 'below-threshold' || escalation.status === 'crosses-threshold') && (
              <>
                <p className="font-semibold text-navy">
                  {formatPercent(escalation.pct)}{' '}
                  <span className="font-normal text-text-faint">
                    (มูลค่า VO สะสม {formatMoney(escalation.cumulativeAmount)} บาท / เกณฑ์ {formatPercent(escalation.thresholdPct)})
                  </span>
                </p>
                {escalation.status === 'crosses-threshold' && (
                  <p
                    role="alert"
                    className="mt-2 rounded border border-warning-text/30 bg-warning-text/10 px-3 py-2 text-[11px] text-warning-text"
                  >
                    VO นี้จะทำให้มูลค่า VO สะสมของโครงการ ({formatPercent(escalation.pct)}) เกินเกณฑ์ escalation ที่ตั้งไว้
                    ({formatPercent(escalation.thresholdPct)})
                    {escalation.role
                      ? ` — ต้องมีขั้นตอนอนุมัติเพิ่มเติมโดย ${ROLE_LABEL[escalation.role as UserRole] ?? escalation.role}`
                      : ' — สายอนุมัติจะมีขั้นตอนเพิ่มเติมตามที่นโยบายกำหนด'}
                  </p>
                )}
              </>
            )}
          </dd>
        </div>
      </dl>
    </div>
  )
}
