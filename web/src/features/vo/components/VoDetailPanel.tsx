import { Button, StatusPill } from '../../../components'
import { cx } from '../../../utils/cx'
import { formatMoney, formatPercent } from '../../../utils/format'
import { VO_STATUS_LABELS, VO_TYPE_LABELS } from '../voStatusLabels'
import { BacImpactPanel } from './BacImpactPanel'
import type { UserRole } from '../../../store/authStore'
import type { VariationOrderDto } from '../types'

function Row({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex items-start justify-between gap-3 border-b border-border-subtle py-2.5 text-[12.5px] last:border-b-0">
      <span className="flex-none text-text-faint">{label}</span>
      <span className="text-right text-text">{value}</span>
    </div>
  )
}

export interface VoDetailPanelProps {
  vo: VariationOrderDto | null
  /** `Project.BAC`/`Project.ContractValue`, current — used only for the *preview* on a not-yet-
   * `Approved` VO. `null` while unknown (see `VoPage.tsx`'s remarks). */
  currentBac: number | null
  currentContractValue: number | null
  cumulativeApprovedVoBefore: number | null
  escalationBaselineContractValue: number | null
  escalationThresholdPct: number | null
  escalationRole?: UserRole | null
  onOpenEdit: () => void
  canEdit: boolean
  onSubmit: () => void
  submitting: boolean
  canSubmit: boolean
  onWithdraw: () => void
  withdrawing: boolean
  canWithdraw: boolean
  onOpenCancel: () => void
  canCancel: boolean
}

/**
 * The VO detail panel (mirrors `features/payment/components/CertificatePanel.tsx`'s sticky right-
 * column shape). Approve/ReturnForRevision/Reject live in `ApprovalChainBar` (rendered alongside the
 * list, `VoPage.tsx`) — this panel owns the VO-lifecycle actions outside the approval chain itself
 * (Submit/Edit/Withdraw/Cancel) plus the S10-FE-02 `BacImpactPanel`.
 *
 * Two distinct `BacImpactPanel` uses, deliberately not conflated:
 * - **Not yet `Approved`**: a live *preview* computed from the project's *current* BAC/ContractValue
 *   — nothing has happened yet, this is "what would happen".
 * - **`Approved`**: the VO's own `bacBefore`/`bacAfter`/`contractValueBefore`/`contractValueAfter`/
 *   `cumulativeVoPctAtApproval` fields, written once at approval and immutable since
 *   (domain-rules.md §2.4) — the *actual*, backend-recorded effect, rendered directly rather than
 *   reconstructed through another preview computation.
 */
export function VoDetailPanel({
  vo,
  currentBac,
  currentContractValue,
  cumulativeApprovedVoBefore,
  escalationBaselineContractValue,
  escalationThresholdPct,
  escalationRole,
  onOpenEdit,
  canEdit,
  onSubmit,
  submitting,
  canSubmit,
  onWithdraw,
  withdrawing,
  canCancel,
  canWithdraw,
  onOpenCancel,
}: VoDetailPanelProps) {
  if (!vo) {
    return (
      <div className="rounded-card border border-border bg-surface p-6 text-center text-xs text-text-faint">
        เลือก Variation Order ทางด้านซ้ายเพื่อดูรายละเอียด
      </div>
    )
  }

  const amount = Number(vo.amount)
  const canSubmitNow = canSubmit && vo.status === 'Draft'
  const canEditNow = canEdit && vo.status === 'Draft'
  const canCancelNow = canCancel && vo.status === 'Draft'
  const canWithdrawNow = canWithdraw && vo.status === 'PendingApproval'

  return (
    <div className="sticky top-0 flex flex-col gap-4">
      <div className="rounded-card border border-border bg-surface p-5">
        <div className="flex flex-wrap items-center gap-2.5 border-b-2 border-navy pb-3">
          <div
            aria-hidden="true"
            className="grid h-[30px] w-[30px] flex-none place-items-center rounded-md bg-gold font-heading text-[10px] font-bold text-navy"
          >
            VO
          </div>
          <div className="font-heading text-sm font-semibold text-navy">{vo.voNumber}</div>
          <span
            className={cx(
              'rounded px-2 py-0.5 text-[10.5px] font-semibold',
              vo.type === 'Add' ? 'bg-secondary/10 text-secondary' : 'bg-danger/10 text-danger',
            )}
          >
            {VO_TYPE_LABELS[vo.type]}
          </span>
          <StatusPill className="ml-auto" label={VO_STATUS_LABELS[vo.status]} status={vo.status} />
        </div>

        <div className="mt-1 flex flex-col">
          <Row label="รายละเอียด" value={vo.description ?? '—'} />
          <Row label="เหตุผล / ที่มา" value={vo.justification ?? '—'} />
          <Row label="มูลค่า VO" value={`${amount > 0 ? '+' : ''}${formatMoney(amount)} บาท`} />
          <Row label="ผลกระทบต่อระยะเวลา" value={vo.timeImpactDays === 0 ? 'ไม่มี' : `${vo.timeImpactDays} วัน`} />
          {vo.revisionNo > 1 && <Row label="ฉบับแก้ไข" value={`ครั้งที่ ${vo.revisionNo} (เคยถูกตีกลับมาแล้ว)`} />}
          {vo.scopeItems.length > 0 && (
            <div className="py-2.5">
              <p className="text-[12.5px] text-text-faint">รายการปรับงบประมาณ (Scope)</p>
              <ul className="mt-1.5 space-y-1">
                {vo.scopeItems.map((item) => (
                  <li key={item.activityId} className="flex items-center justify-between text-[11.5px] text-text-muted">
                    <span className="truncate">
                      {item.activityId}
                      {item.note ? ` — ${item.note}` : ''}
                    </span>
                    <span className={Number(item.budgetCostDelta) < 0 ? 'text-danger' : 'text-text'}>
                      {Number(item.budgetCostDelta) > 0 ? '+' : ''}
                      {formatMoney(item.budgetCostDelta)}
                    </span>
                  </li>
                ))}
              </ul>
            </div>
          )}
        </div>

        <div className="mt-3 flex flex-wrap gap-2">
          {canSubmitNow && (
            <Button size="sm" onClick={onSubmit} loading={submitting}>
              ส่งอนุมัติ
            </Button>
          )}
          {canEditNow && (
            <Button size="sm" variant="secondary" onClick={onOpenEdit} disabled={submitting}>
              แก้ไข
            </Button>
          )}
          {canCancelNow && (
            <Button size="sm" variant="danger" onClick={onOpenCancel} disabled={submitting}>
              ยกเลิก
            </Button>
          )}
          {canWithdrawNow && (
            <Button size="sm" variant="secondary" onClick={onWithdraw} loading={withdrawing}>
              ถอนคำขอ
            </Button>
          )}
        </div>
      </div>

      {vo.status === 'Approved' && vo.bacBefore !== null && vo.bacAfter !== null && (
        <div className="rounded-card border border-border bg-surface p-4">
          <h3 className="font-heading text-sm font-semibold text-navy">ผลกระทบที่บันทึกไว้ตอนอนุมัติ</h3>
          <dl className="mt-3 flex flex-col divide-y divide-border-subtle text-[12.5px]">
            <div className="flex items-center justify-between gap-3 py-2">
              <dt className="text-text-faint">BAC เดิม → ใหม่</dt>
              <dd className="font-semibold text-navy">
                {formatMoney(vo.bacBefore)} → {formatMoney(vo.bacAfter)} บาท
              </dd>
            </div>
            <div className="flex items-center justify-between gap-3 py-2">
              <dt className="text-text-faint">มูลค่าสัญญาเดิม → ใหม่</dt>
              <dd className="font-semibold text-navy">
                {formatMoney(vo.contractValueBefore ?? '0')} → {formatMoney(vo.contractValueAfter ?? '0')} บาท
              </dd>
            </div>
            <div className="flex items-center justify-between gap-3 py-2">
              <dt className="text-text-faint">% VO สะสม ณ วันที่อนุมัติ</dt>
              <dd className="font-semibold text-navy">
                {vo.cumulativeVoPctAtApproval === null ? 'ไม่ได้ตั้งค่าเกณฑ์ escalation' : formatPercent(vo.cumulativeVoPctAtApproval)}
              </dd>
            </div>
          </dl>
        </div>
      )}

      {(vo.status === 'Draft' || vo.status === 'PendingApproval') &&
        (currentBac !== null && currentContractValue !== null ? (
          <BacImpactPanel
            amount={amount}
            bacBefore={currentBac}
            contractValueBefore={currentContractValue}
            cumulativeApprovedVoBefore={cumulativeApprovedVoBefore}
            escalationBaselineContractValue={escalationBaselineContractValue}
            escalationThresholdPct={escalationThresholdPct}
            escalationRole={escalationRole}
            title="ผลกระทบต่อ BAC / มูลค่าสัญญา (ก่อนยืนยัน)"
          />
        ) : (
          // Honest degradation — never substitute 0 for an unknown BAC/ContractValue, which would
          // render a fabricated "0.00 → {amount}" preview. See VoPage.tsx's remarks: no live
          // endpoint reliably returns Project.BAC/ContractValue to every VO-screen role today.
          <div className="rounded-card border border-dashed border-border bg-surface p-4 text-[11px] text-text-faint">
            ยังไม่สามารถแสดงผลกระทบต่อ BAC / มูลค่าสัญญาได้ในขณะนี้ (ไม่มีข้อมูล BAC/มูลค่าสัญญาปัจจุบันของโครงการ)
          </div>
        ))}
    </div>
  )
}
