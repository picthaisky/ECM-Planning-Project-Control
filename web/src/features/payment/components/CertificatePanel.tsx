import { Button, StatusPill } from '../../../components'
import { cx } from '../../../utils/cx'
import { formatMoney, formatPercent } from '../../../utils/format'
import { resolveRetentionCapStatus } from '../certificateBreakdown'
import { PAYMENT_STATUS_LABELS } from '../paymentStatusLabels'
import type { Project } from '../../info'
import type { PaymentCertificateDto } from '../types'

export interface CertificatePanelProps {
  certificate: PaymentCertificateDto | null
  /** `null` while the project master data has not loaded yet (or failed to) — every row that needs
   * a rate/cap/contract figure degrades gracefully rather than blocking on it (conventions.md: every
   * async surface has loading/empty/error states). */
  project: Project | null
  onOpenSettings: () => void
  onSubmit: () => void
  submitting: boolean
  /** UX-only role gate mirroring `PaymentCertificatesController.CertificateCrudRoles`
   * (`QS,PM,ProjectDirector,Admin`) — the server re-checks independently regardless. */
  canSubmit: boolean
}

function Row({
  label,
  value,
  tone,
  bold,
  large,
}: {
  label: string
  value: string
  tone?: 'danger' | 'navy'
  bold?: boolean
  large?: boolean
}) {
  return (
    <div className="flex items-center justify-between gap-3 border-b border-border-subtle py-2.5 text-[12.5px] last:border-b-0">
      <span className="text-text-faint">{label}</span>
      <span
        className={cx(
          bold ? 'font-semibold' : 'font-normal',
          large && 'font-heading text-lg',
          tone === 'danger' ? 'text-danger' : tone === 'navy' ? 'text-navy' : 'text-text',
        )}
      >
        {value}
      </span>
    </div>
  )
}

/**
 * S9-FE-01: the sticky certificate detail panel (prototype screen #7's right column,
 * `docs/ECM Planning Prototype.dc.html` line ~338). The prototype shows only
 * *Milestone Value → Retention → Net*, which overstates Net Payment by the full advance-recovery
 * amount whenever `AdvanceRate` is configured (payment-retention.md §2.6 / domain-decisions.md
 * §2.6) — this panel instead shows the full chain the DoD requires:
 *
 * มูลค่างวดงาน (context) → มูลค่าที่รับรองงวดนี้ (Gross, $G_k$) → หัก Retention (with a cap
 * indicator, `certificateBreakdown.ts`) → หัก Advance Recovery ($D_k$, the fixed gap) → รับสุทธิ
 * (Net, $N_k$).
 *
 * All five money figures are the backend's own already-computed, already-cap-aware values
 * (`PaymentCertificateDto`) — this component never recomputes the certificate formula, only
 * *labels* and *lays out* what the server already calculated (CLAUDE.md: never diverge from the
 * backend's own math).
 */
export function CertificatePanel({
  certificate,
  project,
  onOpenSettings,
  onSubmit,
  submitting,
  canSubmit,
}: CertificatePanelProps) {
  if (!certificate) {
    return (
      <div className="rounded-card border border-border bg-surface p-6 text-center text-xs text-text-faint">
        เลือกงวดงานทางด้านซ้ายเพื่อดูรายละเอียดใบรับรองผลงาน
      </div>
    )
  }

  const retentionRatePct = project?.retentionRate === null || project?.retentionRate === undefined ? null : Number(project.retentionRate)
  const advanceRatePct = project?.advanceRate === null || project?.advanceRate === undefined ? null : Number(project.advanceRate)

  const capStatus = resolveRetentionCapStatus({
    grossCertifiedAmount: Number(certificate.grossCertifiedAmount),
    actualRetentionAmount: Number(certificate.retentionAmount),
    retentionRatePct,
    retentionCapPercentage: project?.retentionCapPercentage === null || project?.retentionCapPercentage === undefined ? null : Number(project.retentionCapPercentage),
    contractValue: project ? Number(project.contractValue) : null,
  })

  const canSubmitNow = canSubmit && certificate.status === 'Draft'

  return (
    <div className="sticky top-0 rounded-card border border-border bg-surface p-5">
      <div className="flex items-center gap-2.5 border-b-2 border-navy pb-3">
        <div
          aria-hidden="true"
          className="grid h-[30px] w-[30px] flex-none place-items-center rounded-md bg-gold font-heading text-[10px] font-bold text-navy"
        >
          ECM
        </div>
        <div className="font-heading text-sm font-semibold text-navy">
          Payment Certificate — งวดที่ {certificate.milestoneNo}
        </div>
        <StatusPill
          className="ml-auto"
          label={PAYMENT_STATUS_LABELS[certificate.status]}
          status={certificate.status}
        />
      </div>

      <div className="flex items-center gap-2 pt-3">
        <button
          type="button"
          onClick={onOpenSettings}
          className="rounded-md border border-border px-3 py-1.5 text-[11px] text-text-muted hover:border-navy hover:text-navy"
        >
          ⚙ ตั้งค่า Retention{retentionRatePct !== null ? ` ${formatPercent(retentionRatePct)}` : ''} / Advance
          {advanceRatePct !== null ? ` ${formatPercent(advanceRatePct)}` : ''}
        </button>
      </div>

      <div className="mt-1 flex flex-col">
        <Row label="รายละเอียดงวด" value={certificate.description ?? '—'} />
        <Row label="มูลค่างวดงาน (Milestone Value)" value={`${formatMoney(certificate.milestoneValue)} บาท`} bold />
        <Row
          label="% อนุมัติสะสม (คราวก่อน → คราวนี้)"
          value={`${formatPercent(certificate.previousCumulativeApprovePct)} → ${formatPercent(certificate.approvePct)}`}
        />

        <Row
          label="มูลค่าที่รับรองงวดนี้ (Gross)"
          value={`${formatMoney(certificate.grossCertifiedAmount)} บาท`}
          tone="navy"
          bold
        />

        <div className="border-b border-border-subtle py-2.5 last:border-b-0">
          <div className="flex items-center justify-between gap-3 text-[12.5px]">
            <span className="text-text-faint">
              หัก Retention{retentionRatePct !== null ? ` ${formatPercent(retentionRatePct)}` : ''}
            </span>
            <span className="font-semibold text-danger">−{formatMoney(certificate.retentionAmount)} บาท</span>
          </div>
          {capStatus.kind === 'uncapped' && (
            <p className="mt-1 text-[10.5px] text-text-faint">ไม่มีเพดาน Retention (มาตรฐานไทย)</p>
          )}
          {capStatus.kind === 'capped' && (
            <p className="mt-1 text-[10.5px] text-text-faint">
              เพดาน Retention สะสมทั้งโครงการ: {formatMoney(capStatus.capAmount)} บาท
              {capStatus.isBindingThisPeriod && (
                <span className="ml-1 font-semibold text-warning-text">— งวดนี้ถูกจำกัดโดยเพดาน</span>
              )}
            </p>
          )}
        </div>

        {/* The fixed gap: the prototype's certificate panel has no advance-recovery line at all
         * (domain-decisions.md §2.6), so its Net Payment reads high by exactly this amount whenever
         * an advance is configured. Always rendered — including "0.00" — never hidden, so a genuine
         * zero this period is never confused with "the line doesn't exist". */}
        <Row
          label={`หัก Advance Recovery${advanceRatePct !== null ? ` ${formatPercent(advanceRatePct)}` : ''}`}
          value={`−${formatMoney(certificate.advanceRecoveryAmount)} บาท`}
          tone="danger"
        />

        <Row label="รับสุทธิ (Net Payment)" value={`${formatMoney(certificate.netPayment)} บาท`} tone="navy" bold large />
      </div>

      {canSubmitNow && (
        <div className="mt-3 flex gap-2">
          <Button className="flex-1" onClick={onSubmit} loading={submitting}>
            ส่งอนุมัติ
          </Button>
        </div>
      )}
    </div>
  )
}
