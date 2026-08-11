import { describe, expect, it, vi } from 'vitest'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ApprovalChainBar } from './ApprovalChainBar'
import type { ApprovalChainBarProps } from './ApprovalChainBar'
import type { ApprovalActionDto } from '../types'

const noop = () => Promise.resolve(null)

function makeProps(overrides: Partial<ApprovalChainBarProps> = {}): ApprovalChainBarProps {
  return {
    statusLabel: 'รออนุมัติ',
    statusValue: 'PendingApproval',
    totalSteps: 3,
    currentStepNo: 2,
    notSubmittedYet: false,
    stepTone: (stepNo) => (stepNo < 2 ? 'done' : stepNo === 2 ? 'current' : 'pending'),
    history: [],
    historyState: 'ready',
    historyUnavailableReason: null,
    quorumPendingNotice: false,
    canApprove: false,
    canReturnForRevision: false,
    canReject: false,
    busy: false,
    actionError: null,
    onApprove: noop,
    onReturnForRevision: noop,
    onReject: noop,
    ...overrides,
  }
}

describe('ApprovalChainBar', () => {
  it('shows "ยังไม่ได้ส่งขออนุมัติ" and no step chain/buttons before the first submit', () => {
    render(
      <ApprovalChainBar
        {...makeProps({ notSubmittedYet: true, totalSteps: 0, currentStepNo: 0, statusLabel: 'ฉบับร่าง', statusValue: 'Draft' })}
      />,
    )

    expect(screen.getByText('ยังไม่ได้ส่งขออนุมัติเอกสารฉบับนี้')).toBeInTheDocument()
    expect(screen.queryByRole('list', { name: 'ขั้นตอนการอนุมัติ' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'อนุมัติ' })).not.toBeInTheDocument()
  })

  it('renders a custom "not submitted" message and title when supplied', () => {
    render(
      <ApprovalChainBar
        {...makeProps({
          notSubmittedYet: true,
          totalSteps: 0,
          currentStepNo: 0,
          title: 'สายอนุมัติ VO',
          notSubmittedMessage: 'ยังไม่ได้ส่ง VO ฉบับนี้เพื่อขออนุมัติ',
        })}
      />,
    )

    expect(screen.getByText('สายอนุมัติ VO')).toBeInTheDocument()
    expect(screen.getByText('ยังไม่ได้ส่ง VO ฉบับนี้เพื่อขออนุมัติ')).toBeInTheDocument()
  })

  it('renders one step marker per totalSteps and names the current step', () => {
    render(<ApprovalChainBar {...makeProps()} />)

    const stepList = screen.getByRole('list', { name: 'ขั้นตอนการอนุมัติ' })
    expect(within(stepList).getAllByRole('listitem')).toHaveLength(3)
    expect(screen.getByText('ขั้นตอนที่ 2 จาก 3')).toBeInTheDocument()
  })

  it('renders an escalation note when supplied, and omits the box entirely when not', () => {
    const { rerender } = render(
      <ApprovalChainBar
        {...makeProps({ escalationNote: 'มูลค่า VO สะสม 10.14% เกินเกณฑ์ 10.00% — เพิ่มขั้นตอนอนุมัติโดย Executive' })}
      />,
    )
    expect(screen.getByText(/มูลค่า VO สะสม 10.14%/)).toBeInTheDocument()

    rerender(<ApprovalChainBar {...makeProps({ escalationNote: null })} />)
    expect(screen.queryByText(/เกินเกณฑ์/)).not.toBeInTheDocument()
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

    render(<ApprovalChainBar {...makeProps({ history })} />)

    expect(screen.getByText('อนุมัติ', { selector: 'span.font-semibold' })).toBeInTheDocument()
    expect(screen.getByText(/QS \/ Cost Engineer/)).toBeInTheDocument()
    expect(screen.getByText('“ตรวจแล้วถูกต้อง”')).toBeInTheDocument()
  })

  it('shows an honest "not available" note instead of fake history when the endpoint is unavailable', () => {
    render(
      <ApprovalChainBar
        {...makeProps({ history: null, historyState: 'unavailable', historyUnavailableReason: 'ไม่พบข้อมูลที่ระบุ' })}
      />,
    )

    expect(screen.getByText(/ยังไม่สามารถโหลดรายละเอียดผู้อนุมัติ/)).toBeInTheDocument()
  })

  it('shows the quorum-pending banner (default message) instead of silence when the document is unchanged after an approval', () => {
    render(<ApprovalChainBar {...makeProps({ quorumPendingNotice: true })} />)

    expect(screen.getByRole('status')).toHaveTextContent('ยังต้องการผู้อนุมัติเพิ่มเติม')
  })

  it('a caller-supplied quorumPendingMessage overrides the default (e.g. VO reject-quorum wording, ADR-0016)', () => {
    render(
      <ApprovalChainBar
        {...makeProps({
          quorumPendingNotice: true,
          quorumPendingMessage: 'บันทึกการปฏิเสธของคุณแล้ว แต่เอกสารยังไม่ถูกปฏิเสธ — ยังต้องการผู้ปฏิเสธเพิ่มเติมให้ครบ Quorum',
        })}
      />,
    )

    expect(screen.getByRole('status')).toHaveTextContent('เอกสารยังไม่ถูกปฏิเสธ')
  })

  describe('button visibility', () => {
    it('renders only the buttons the caller marks as attemptable', () => {
      render(<ApprovalChainBar {...makeProps({ canApprove: false, canReturnForRevision: true, canReject: false })} />)

      expect(screen.queryByRole('button', { name: 'อนุมัติ' })).not.toBeInTheDocument()
      expect(screen.getByRole('button', { name: 'ตีกลับแก้ไข' })).toBeInTheDocument()
      expect(screen.queryByRole('button', { name: 'ปฏิเสธ' })).not.toBeInTheDocument()
    })

    it('renders all three when every flag is true, using custom labels when supplied', () => {
      render(
        <ApprovalChainBar
          {...makeProps({
            canApprove: true,
            canReturnForRevision: true,
            canReject: true,
            rejectLabel: 'ปฏิเสธ VO',
          })}
        />,
      )

      expect(screen.getByRole('button', { name: 'อนุมัติ' })).toBeInTheDocument()
      expect(screen.getByRole('button', { name: 'ตีกลับแก้ไข' })).toBeInTheDocument()
      expect(screen.getByRole('button', { name: 'ปฏิเสธ VO' })).toBeInTheDocument()
    })

    it('hides every action button once the caller determines none is attemptable (e.g. a terminal status)', () => {
      render(<ApprovalChainBar {...makeProps({ canApprove: false, canReturnForRevision: false, canReject: false })} />)

      expect(screen.queryByRole('button', { name: 'อนุมัติ' })).not.toBeInTheDocument()
      expect(screen.queryByRole('button', { name: 'ตีกลับแก้ไข' })).not.toBeInTheDocument()
      expect(screen.queryByRole('button', { name: 'ปฏิเสธ' })).not.toBeInTheDocument()
    })
  })

  describe('modal flow', () => {
    it('opens the approve modal, confirms, and calls onApprove with the entered comment', async () => {
      const onApprove = vi.fn().mockResolvedValue({ ok: true })
      render(<ApprovalChainBar {...makeProps({ canApprove: true, onApprove })} />)

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
          {...makeProps({
            canReject: true,
            onReject,
            actionError: 'คุณไม่มีสิทธิ์ดำเนินการกับขั้นตอนอนุมัติปัจจุบันของเอกสารนี้',
          })}
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
      render(<ApprovalChainBar {...makeProps({ canReturnForRevision: true })} />)

      await userEvent.click(screen.getByRole('button', { name: 'ตีกลับแก้ไข' }))
      expect(screen.getByRole('dialog')).toBeInTheDocument()
      await userEvent.click(screen.getByRole('button', { name: 'ยกเลิก' }))
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    })
  })
})
