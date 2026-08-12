import { StatusPill } from '../../../components'
import { formatPercent } from '../../../utils/format'
import { ROLE_LABEL } from '../../../utils/roleLabels'
import type { ApprovalPolicyHistoryLoadState } from '../useApprovalPolicyHistory'
import type { ApprovalDocumentType, ApprovalPolicyVersionHistoryEntry } from '../types'

const dateTimeFormatter = new Intl.DateTimeFormat('th-TH', {
  dateStyle: 'medium',
  timeStyle: 'short',
  timeZone: 'Asia/Bangkok',
})

function formatDateTime(value: string | null): string {
  if (!value) return 'ไม่ทราบเวลา'
  const parsed = new Date(value)
  return Number.isNaN(parsed.getTime()) ? value : dateTimeFormatter.format(parsed)
}

export interface PolicyHistoryTimelineProps {
  documentType: ApprovalDocumentType
  entries: ApprovalPolicyVersionHistoryEntry[]
  loadState: ApprovalPolicyHistoryLoadState
  loadError: string | null
  onRetry: () => void
}

/**
 * S15-FE-01 "Admin เห็นลำดับเวลาการเปลี่ยน policy" (US-15.2) — reads `GET .../{documentType}/history`
 * (S15-BE-01), which the backend assembles purely from `ApprovalPolicy` (every version, never
 * deleted — only `IsActive`/`EffectiveTo` flip) + `AuditLog` (who/when, already written for every
 * Create/Update by the standing `AuditSaveChangesInterceptor`) — **no new storage on either side**.
 *
 * Rendered newest-first: the version an Admin almost always cares about right after a change —
 * "what did I (or someone else) just change, and when" — sits at the top, without scrolling past
 * the full history first (the prototype's own activity-style feeds follow the same convention).
 */
export function PolicyHistoryTimeline({
  documentType,
  entries,
  loadState,
  loadError,
  onRetry,
}: PolicyHistoryTimelineProps) {
  const isVariationOrder = documentType === 'VariationOrder'
  const ordered = [...entries].sort((a, b) => b.version - a.version)

  return (
    <div className="rounded-card border border-border bg-surface p-4" data-testid="approval-policy-history">
      <div className="mb-3 flex items-center">
        <h3 className="font-heading text-[13.5px] font-semibold text-navy">ประวัติเวอร์ชันนโยบาย (Version History)</h3>
        <button
          type="button"
          onClick={onRetry}
          disabled={loadState === 'loading'}
          className="ml-auto text-[10.5px] font-medium text-navy underline decoration-dotted disabled:cursor-not-allowed disabled:opacity-50"
        >
          รีเฟรช
        </button>
      </div>

      {loadState === 'loading' && (
        <p role="status" className="py-6 text-center text-xs text-text-faint">
          กำลังโหลดประวัติเวอร์ชันนโยบาย...
        </p>
      )}

      {loadState === 'error' && (
        <p role="alert" className="py-6 text-center text-xs text-danger">
          {loadError ?? 'โหลดประวัติเวอร์ชันนโยบายไม่สำเร็จ'}
        </p>
      )}

      {loadState === 'ready' && ordered.length === 0 && (
        <p className="py-6 text-center text-xs text-text-faint">
          ยังไม่มีประวัติการเปลี่ยนแปลงนโยบายสำหรับประเภทเอกสารนี้
        </p>
      )}

      {loadState === 'ready' && ordered.length > 0 && (
        <ol aria-label="ประวัติเวอร์ชันนโยบายตามลำดับเวลา" className="relative flex flex-col gap-4 border-l border-border pl-4">
          {ordered.map((entry) => {
            const changedAt = entry.lastModifiedAt ?? entry.createdAt
            const changedBy = entry.lastModifiedByUserId ?? entry.createdByUserId
            return (
              <li key={entry.approvalPolicyId} className="relative">
                <span
                  aria-hidden="true"
                  className={`absolute -left-[21px] top-1 h-2.5 w-2.5 rounded-full border-2 border-surface ${
                    entry.isActive ? 'bg-gold' : 'bg-border'
                  }`}
                />
                <div className="flex flex-wrap items-center gap-2">
                  <span className="font-heading text-[13px] font-semibold text-navy">v{entry.version}</span>
                  {entry.isActive && <StatusPill label="ใช้งานอยู่ (Active)" tone="success" />}
                  <span className="text-[10.5px] text-text-faint">{formatDateTime(changedAt)}</span>
                </div>
                <div className="mt-0.5 text-[11px] text-text-faint">
                  {changedBy
                    ? `แก้ไขโดยผู้ใช้รหัส ${changedBy}`
                    : 'ไม่ทราบผู้แก้ไข (ไม่พบข้อมูล audit log สำหรับเวอร์ชันนี้ เช่น เวอร์ชันที่สร้างโดยระบบตอนตั้งค่า tenant)'}
                </div>
                {/* Deliberately `<div>`/`<span>`, not a nested `<ul>` — a real list here would make
                    every consumer's `getAllByRole('listitem')` on the outer timeline also pick up
                    these three metadata fragments per entry (a real bug caught by this component's
                    own test), and there is nothing here a screen reader user would navigate as a
                    list. */}
                <div className="mt-1.5 flex flex-wrap gap-x-4 gap-y-0.5 text-[11px] text-text-muted">
                  <span>{entry.ruleCount.toLocaleString('th-TH')} ขั้นตอนอนุมัติ</span>
                  <span>{entry.allowSelfApproval ? 'อนุญาตอนุมัติเอกสารของตนเอง' : 'ไม่อนุญาตอนุมัติเอกสารของตนเอง'}</span>
                  {isVariationOrder && (
                    <span>
                      {entry.cumulativeVoEscalationPct
                        ? `Escalation สะสม ${formatPercent(entry.cumulativeVoEscalationPct)} → ${
                            entry.cumulativeVoEscalationRole ? ROLE_LABEL[entry.cumulativeVoEscalationRole] : '—'
                          }`
                        : 'ไม่ใช้ escalation สะสม'}
                    </span>
                  )}
                </div>
                <div className="mt-0.5 text-[10px] text-text-faint">
                  มีผลตั้งแต่ {formatDateTime(entry.effectiveFrom)}
                  {entry.effectiveTo ? ` ถึง ${formatDateTime(entry.effectiveTo)}` : ' — ปัจจุบัน'}
                </div>
              </li>
            )
          })}
        </ol>
      )}
    </div>
  )
}
