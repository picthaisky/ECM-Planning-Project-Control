import type { PaymentCertificateStatus } from './types'

/**
 * Thai display text for `PaymentCertificateStatus` (approval-workflow.md §3). `Certified`/`Paid`
 * use the exact Thai terms the domain doc pins to the prototype's own badges ("ตรวจรับแล้ว
 * → Certified, จ่ายแล้ว → Paid") — never re-translated ad hoc. Colour comes separately from
 * `components/statusTone.ts#resolveStatusTone` (never embedded here — "one fact lives in one place").
 */
export const PAYMENT_STATUS_LABELS: Record<PaymentCertificateStatus, string> = {
  NotDue: 'ยังไม่ถึงกำหนด',
  Draft: 'ฉบับร่าง',
  PendingApproval: 'รออนุมัติ',
  Certified: 'ตรวจรับแล้ว',
  Paid: 'จ่ายแล้ว',
  Rejected: 'ปฏิเสธ',
  Cancelled: 'ยกเลิก',
}
