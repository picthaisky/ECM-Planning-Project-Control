import { Button, Modal } from '../../components'
import { formatPercent } from '../../utils/format'
import type { ProgressRow } from './useBatchProgressForm'

export interface DecreaseConfirmModalProps {
  isOpen: boolean
  rows: ProgressRow[]
  onCancel: () => void
  onConfirm: () => void
  confirming: boolean
}

/**
 * S4-FE-03 DoD: entering a progress value lower than the current one requires an explicit
 * "ยืนยันการปรับลดความคืบหน้า" confirmation before the batch is submitted. Lists every affected
 * row so the person confirming can see exactly what they are reducing, not just a generic warning.
 */
export function DecreaseConfirmModal({ isOpen, rows, onCancel, onConfirm, confirming }: DecreaseConfirmModalProps) {
  return (
    <Modal
      isOpen={isOpen}
      onClose={onCancel}
      title="ยืนยันการปรับลดความคืบหน้า"
      footer={
        <>
          <Button variant="secondary" size="sm" onClick={onCancel} disabled={confirming}>
            ยกเลิก
          </Button>
          <Button variant="danger" size="sm" onClick={onConfirm} loading={confirming}>
            ยืนยันการปรับลด
          </Button>
        </>
      }
    >
      <p className="text-xs text-text-muted">
        รายการต่อไปนี้มีค่า % ความคืบหน้าใหม่ต่ำกว่าค่าปัจจุบัน กรุณายืนยันว่าต้องการปรับลดจริง:
      </p>
      <ul className="mt-3 space-y-1.5 text-xs">
        {rows.map((row) => (
          <li
            key={row.activityId}
            className="flex items-center justify-between rounded border border-border-subtle px-2.5 py-1.5"
          >
            <span className="text-text">{row.activityCode ?? row.activityId}</span>
            <span className="text-danger">
              {formatPercent(row.currentProgressPercentage ?? '0')} → {formatPercent(row.newProgressPercentage || '0')}
            </span>
          </li>
        ))}
      </ul>
    </Modal>
  )
}
