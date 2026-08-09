import { describe, expect, it, vi } from 'vitest'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ApprovalChainBar } from './ApprovalChainBar'
import type { ApprovalActionDto, PaymentCertificateDto } from '../types'

function makeCertificate(overrides: Partial<PaymentCertificateDto> = {}): PaymentCertificateDto {
  return {
    id: 'cert-1',
    projectId: 'project-1',
    milestoneNo: 3,
    description: 'งานโครงสร้าง',
    milestoneValue: '21600000.00',
    previousCumulativeApprovePct: '0.00',
    approvePct: '100.00',
    claimPct: '100.00',
    actualProgressPct: '100.00',
    grossCertifiedAmount: '21600000.00',
    retentionAmount: '1080000.00',
    advanceRecoveryAmount: '2160000.00',
    netPayment: '18360000.00',
    status: 'PendingApproval',
    revisionNo: 1,
    currentStepNo: 2,
    totalSteps: 3,
    approvalPolicyId: 'policy-1',
    approvalPolicyVersion: 1,
    createdByUserId: 'creator-1',
    submittedByUserId: 'creator-1',
    submittedAt: '2026-08-01T00:00:00+07:00',
    certifiedAt: null,
    paidAt: null,
    paymentReference: null,
    ...overrides,
  }
}

const noop = () => Promise.resolve(null)

describe('ApprovalChainBar', () => {
  it('shows "ยังไม่ได้ส่งขออนุมัติ" and no step chain/buttons before the first submit', () => {
    render(
      <ApprovalChainBar
        certificate={makeCertificate({ status: 'Draft', currentStepNo: 0, totalSteps: 0 })}
        history={null}
        historyState="unavailable"
        historyUnavailableReason={null}
        currentUserId="user-x"
        busyAction={null}
        actionError={null}
        quorumPendingNotice={false}
        onApprove={noop}
        onReturnForRevision={noop}
        onReject={noop}
      />,
    )

    expect(screen.getByText('ยังไม่ได้ส่งขออนุมัติเอกสารฉบับนี้')).toBeInTheDocument()
    expect(screen.queryByRole('list', { name: 'ขั้นตอนการอนุมัติ' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'อนุมัติ' })).not.toBeInTheDocument()
  })

  it('renders one step marker per totalSteps and names the current step', () => {
    render(
      <ApprovalChainBar
        certificate={makeCertificate()}
        history={[]}
        historyState="ready"
        historyUnavailableReason={null}
        currentUserId="unrelated-user"
        busyAction={null}
        actionError={null}
        quorumPendingNotice={false}
        onApprove={noop}
        onReturnForRevision={noop}
        onReject={noop}
      />,
    )

    const stepList = screen.getByRole('list', { name: 'ขั้นตอนการอนุมัติ' })
    expect(within(stepList).getAllByRole('listitem')).toHaveLength(3)
    expect(screen.getByText('ขั้นตอนที่ 2 จาก 3')).toBeInTheDocument()
  })

  it('renders history rows with action label, step, role, time, and comment when available', () => {
    const history: ApprovalActionDto[] = [
      {
        id: 'action-1',
        documentType: 'PaymentCertificate',
        documentId: 'cert-1',
        revisionNo: 1,
        stepNo: 1,
        actorUserId: 'user-2',
        actorRoleAtTime: 'QS',
        action: 'Approve',
        comment: 'ตรวจแล้วถูกต้อง',
        actedAt: '2026-08-02T10:00:00+07:00',
        approvalPolicyId: 'policy-1',
        approvalPolicyVersion: 1,
      },
    ]

    render(
      <ApprovalChainBar
        certificate={makeCertificate()}
        history={history}
        historyState="ready"
        historyUnavailableReason={null}
        currentUserId="unrelated-user"
        busyAction={null}
        actionError={null}
        quorumPendingNotice={false}
        onApprove={noop}
        onReturnForRevision={noop}
        onReject={noop}
      />,
    )

    expect(screen.getByText('อนุมัติ', { selector: 'span.font-semibold' })).toBeInTheDocument()
    expect(screen.getByText(/QS \/ Cost Engineer/)).toBeInTheDocument()
    expect(screen.getByText('“ตรวจแล้วถูกต้อง”')).toBeInTheDocument()
  })

  it('shows an honest "not available" note instead of fake history when the endpoint is unavailable', () => {
    render(
      <ApprovalChainBar
        certificate={makeCertificate()}
        history={null}
        historyState="unavailable"
        historyUnavailableReason="ไม่พบข้อมูลที่ระบุ"
        currentUserId="unrelated-user"
        busyAction={null}
        actionError={null}
        quorumPendingNotice={false}
        onApprove={noop}
        onReturnForRevision={noop}
        onReject={noop}
      />,
    )

    expect(screen.getByText(/ยังไม่สามารถโหลดรายละเอียดผู้อนุมัติ/)).toBeInTheDocument()
  })

  it('shows the quorum-pending banner instead of silence when the certificate is unchanged after an approval', () => {
    render(
      <ApprovalChainBar
        certificate={makeCertificate()}
        history={[]}
        historyState="ready"
        historyUnavailableReason={null}
        currentUserId="unrelated-user"
        busyAction={null}
        actionError={null}
        quorumPendingNotice
        onApprove={noop}
        onReturnForRevision={noop}
        onReject={noop}
      />,
    )

    expect(screen.getByRole('status')).toHaveTextContent('ยังต้องการผู้อนุมัติเพิ่มเติม')
  })

  describe('button visibility', () => {
    it('hides Approve for the creator/submitter, still shows ตีกลับแก้ไข, hides ปฏิเสธ off the final step', () => {
      render(
        <ApprovalChainBar
          certificate={makeCertificate()} // currentStepNo 2 of 3 — not final; createdBy/submittedBy = creator-1
          history={[]}
          historyState="ready"
          historyUnavailableReason={null}
          currentUserId="creator-1"
          busyAction={null}
          actionError={null}
          quorumPendingNotice={false}
          onApprove={noop}
          onReturnForRevision={noop}
          onReject={noop}
        />,
      )

      expect(screen.queryByRole('button', { name: 'อนุมัติ' })).not.toBeInTheDocument()
      expect(screen.getByRole('button', { name: 'ตีกลับแก้ไข' })).toBeInTheDocument()
      expect(screen.queryByRole('button', { name: 'ปฏิเสธ' })).not.toBeInTheDocument()
    })

    it('shows ปฏิเสธ only at the structurally-final step, for an unrelated user', () => {
      render(
        <ApprovalChainBar
          certificate={makeCertificate({ currentStepNo: 3, totalSteps: 3 })}
          history={[]}
          historyState="ready"
          historyUnavailableReason={null}
          currentUserId="final-approver"
          busyAction={null}
          actionError={null}
          quorumPendingNotice={false}
          onApprove={noop}
          onReturnForRevision={noop}
          onReject={noop}
        />,
      )

      expect(screen.getByRole('button', { name: 'อนุมัติ' })).toBeInTheDocument()
      expect(screen.getByRole('button', { name: 'ปฏิเสธ' })).toBeInTheDocument()
    })

    it('hides every action button once the document leaves PendingApproval', () => {
      render(
        <ApprovalChainBar
          certificate={makeCertificate({ status: 'Certified', currentStepNo: 3, totalSteps: 3 })}
          history={[]}
          historyState="ready"
          historyUnavailableReason={null}
          currentUserId="unrelated-user"
          busyAction={null}
          actionError={null}
          quorumPendingNotice={false}
          onApprove={noop}
          onReturnForRevision={noop}
          onReject={noop}
        />,
      )

      expect(screen.queryByRole('button', { name: 'อนุมัติ' })).not.toBeInTheDocument()
      expect(screen.queryByRole('button', { name: 'ตีกลับแก้ไข' })).not.toBeInTheDocument()
      expect(screen.queryByRole('button', { name: 'ปฏิเสธ' })).not.toBeInTheDocument()
    })
  })

  describe('modal flow', () => {
    it('opens the approve modal, confirms, and calls onApprove with the entered comment', async () => {
      const onApprove = vi.fn().mockResolvedValue(makeCertificate())
      render(
        <ApprovalChainBar
          certificate={makeCertificate({ currentStepNo: 3, totalSteps: 3 })}
          history={[]}
          historyState="ready"
          historyUnavailableReason={null}
          currentUserId="final-approver"
          busyAction={null}
          actionError={null}
          quorumPendingNotice={false}
          onApprove={onApprove}
          onReturnForRevision={noop}
          onReject={noop}
        />,
      )

      await userEvent.click(screen.getByRole('button', { name: 'อนุมัติ' }))
      const dialog = screen.getByRole('dialog')

      // The modal's own confirm button shares the "อนุมัติ" label with the trigger button behind
      // it — scope the query to the dialog to disambiguate.
      await userEvent.click(within(dialog).getByRole('button', { name: 'อนุมัติ' }))
      expect(onApprove).toHaveBeenCalledWith('')
    })

    it('keeps the modal open and shows the server error when the action fails', async () => {
      const onReject = vi.fn().mockResolvedValue(null)
      render(
        <ApprovalChainBar
          certificate={makeCertificate({ currentStepNo: 3, totalSteps: 3 })}
          history={[]}
          historyState="ready"
          historyUnavailableReason={null}
          currentUserId="final-approver"
          busyAction={null}
          actionError="คุณไม่มีสิทธิ์ดำเนินการกับขั้นตอนอนุมัติปัจจุบันของเอกสารนี้"
          quorumPendingNotice={false}
          onApprove={noop}
          onReturnForRevision={noop}
          onReject={onReject}
        />,
      )

      await userEvent.click(screen.getByRole('button', { name: 'ปฏิเสธ' }))
      await userEvent.type(screen.getByPlaceholderText('เหตุผลที่ปฏิเสธ (บังคับกรอก)'), 'เหตุผล')
      await userEvent.click(screen.getByRole('button', { name: 'ยืนยันปฏิเสธ' }))

      expect(onReject).toHaveBeenCalledWith('เหตุผล')
      expect(screen.getByRole('dialog')).toBeInTheDocument()
      expect(screen.getByRole('alert')).toHaveTextContent('คุณไม่มีสิทธิ์ดำเนินการกับขั้นตอนอนุมัติปัจจุบันของเอกสารนี้')
    })

    it('cancel closes the modal without calling the action', async () => {
      render(
        <ApprovalChainBar
          certificate={makeCertificate({ currentStepNo: 3, totalSteps: 3 })}
          history={[]}
          historyState="ready"
          historyUnavailableReason={null}
          currentUserId="final-approver"
          busyAction={null}
          actionError={null}
          quorumPendingNotice={false}
          onApprove={noop}
          onReturnForRevision={noop}
          onReject={noop}
        />,
      )

      await userEvent.click(screen.getByRole('button', { name: 'ตีกลับแก้ไข' }))
      expect(screen.getByRole('dialog')).toBeInTheDocument()
      await userEvent.click(screen.getByRole('button', { name: 'ยกเลิก' }))
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    })
  })
})
