import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ApprovalPolicyEditorForm } from './ApprovalPolicyEditorForm'
import { MAX_CLIENT_QUORUM_COUNT } from '../bandValidation'
import type { ApprovalPolicy } from '../types'

const policy: ApprovalPolicy = {
  documentType: 'PaymentCertificate',
  version: 3,
  isActive: true,
  allowSelfApproval: false,
  cumulativeVoEscalationPct: null,
  cumulativeVoEscalationRole: null,
  rules: [
    { stepNo: 1, minAmount: '0.00', maxAmount: null, requiredRole: 'QS', quorumCount: 1 },
    { stepNo: 2, minAmount: '10000000.00', maxAmount: null, requiredRole: 'ProjectDirector', quorumCount: 2 },
  ],
}

const defaultProps = {
  documentType: 'PaymentCertificate' as const,
  policy,
  loadState: 'ready' as const,
  loadError: null,
  saveState: 'idle' as const,
  saveError: null,
  savedVersion: null,
  onDismissSavedVersion: vi.fn(),
  onSave: vi.fn(),
}

describe('ApprovalPolicyEditorForm', () => {
  it('loading state', () => {
    render(<ApprovalPolicyEditorForm {...defaultProps} loadState="loading" policy={null} />)
    expect(screen.getByText('กำลังโหลดนโยบายอนุมัติ...')).toBeInTheDocument()
  })

  it('error state', () => {
    render(<ApprovalPolicyEditorForm {...defaultProps} loadState="error" policy={null} loadError="โหลดไม่สำเร็จ" />)
    expect(screen.getByRole('alert')).toHaveTextContent('โหลดไม่สำเร็จ')
  })

  it('not-configured: shows a "no policy yet" note and seeds one blank default row instead of blocking', () => {
    render(<ApprovalPolicyEditorForm {...defaultProps} loadState="not-configured" policy={null} />)
    expect(screen.getByText(/ยังไม่มีการตั้งค่านโยบายอนุมัติ/)).toBeInTheDocument()
    expect(screen.getAllByRole('row')).toHaveLength(2) // header + 1 blank rule row
  })

  it('ready: seeds the draft from the loaded policy, pre-filled', () => {
    render(<ApprovalPolicyEditorForm {...defaultProps} />)
    expect(screen.getByText('v3')).toBeInTheDocument()
    expect(screen.getByDisplayValue('10000000.00')).toBeInTheDocument()
    expect(screen.getAllByRole('row')).toHaveLength(3) // header + 2 rules
  })

  it('shows the escalation fields only for VariationOrder, never for PaymentCertificate', () => {
    const { rerender } = render(<ApprovalPolicyEditorForm {...defaultProps} documentType="PaymentCertificate" />)
    expect(screen.queryByText('Cumulative VO Escalation (%)')).not.toBeInTheDocument()

    rerender(<ApprovalPolicyEditorForm {...defaultProps} documentType="VariationOrder" />)
    expect(screen.getByText('Cumulative VO Escalation (%)')).toBeInTheDocument()
  })

  it('add/remove rows, and the last remaining row cannot be removed', async () => {
    render(<ApprovalPolicyEditorForm {...defaultProps} policy={{ ...policy, rules: [policy.rules[0]] }} />)
    expect(screen.getAllByRole('row')).toHaveLength(2)

    await userEvent.click(screen.getByRole('button', { name: '+ เพิ่มขั้นตอน' }))
    expect(screen.getAllByRole('row')).toHaveLength(3)

    expect(screen.getByRole('button', { name: 'ลบขั้นตอนแถวที่ 1' })).toBeEnabled()
    await userEvent.click(screen.getByRole('button', { name: 'ลบขั้นตอนแถวที่ 2' }))
    expect(screen.getAllByRole('row')).toHaveLength(2)
    expect(screen.getByRole('button', { name: 'ลบขั้นตอนแถวที่ 1' })).toBeDisabled()
  })

  it('flags a band overlap inline and disables Save until it is fixed', async () => {
    render(
      <ApprovalPolicyEditorForm
        {...defaultProps}
        policy={{
          ...policy,
          rules: [
            { stepNo: 1, minAmount: '0.00', maxAmount: null, requiredRole: 'QS', quorumCount: 1 },
            { stepNo: 1, minAmount: '0.00', maxAmount: null, requiredRole: 'PM', quorumCount: 1 },
          ],
        }}
      />,
    )

    expect(screen.getByText(/มีช่วงจำนวนเงินทับซ้อนกัน/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'บันทึกนโยบาย' })).toBeDisabled()
  })

  it('rejects a QuorumCount above the client-side foot-gun bound and explains why, without claiming it is enforced server-side', async () => {
    render(<ApprovalPolicyEditorForm {...defaultProps} policy={{ ...policy, rules: [policy.rules[0]] }} />)

    // Locate the quorum input robustly by its current value/max attribute instead of a brittle index.
    const inputs = screen.getAllByRole('spinbutton')
    const quorumField = inputs.find((el) => (el as HTMLInputElement).value === '1' && el.getAttribute('max') === String(MAX_CLIENT_QUORUM_COUNT))
    expect(quorumField).toBeDefined()

    await userEvent.clear(quorumField as HTMLInputElement)
    await userEvent.type(quorumField as HTMLInputElement, String(MAX_CLIENT_QUORUM_COUNT + 1))

    expect(screen.getByText(new RegExp(`Quorum สูงสุด ${MAX_CLIENT_QUORUM_COUNT}`))).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'บันทึกนโยบาย' })).toBeDisabled()
    // The persistent explanatory note (always shown, not just on error) makes clear this is a UI
    // guard, not server enforcement.
    expect(screen.getByText(/ระบบฝั่งเซิร์ฟเวอร์ยังไม่ได้บังคับเพดานนี้/)).toBeInTheDocument()
  })

  it('calls onSave with the full payload shape when the draft is valid', async () => {
    const onSave = vi.fn()
    render(<ApprovalPolicyEditorForm {...defaultProps} onSave={onSave} />)

    await userEvent.click(screen.getByRole('button', { name: 'บันทึกนโยบาย' }))

    expect(onSave).toHaveBeenCalledWith({
      allowSelfApproval: false,
      cumulativeVoEscalationPct: null,
      cumulativeVoEscalationRole: null,
      rules: policy.rules,
    })
  })

  it('shows the new version banner after a successful save and dismisses on click', async () => {
    const onDismiss = vi.fn()
    render(<ApprovalPolicyEditorForm {...defaultProps} savedVersion={4} onDismissSavedVersion={onDismiss} />)

    expect(screen.getByRole('status')).toHaveTextContent('v4')
    await userEvent.click(screen.getByRole('button', { name: '×' }))
    expect(onDismiss).toHaveBeenCalledTimes(1)
  })

  it('shows the save error message when the server rejects the save', () => {
    render(<ApprovalPolicyEditorForm {...defaultProps} saveState="error" saveError="พบช่วงจำนวนเงินที่ทับซ้อนกัน" />)
    expect(screen.getByText('พบช่วงจำนวนเงินที่ทับซ้อนกัน')).toBeInTheDocument()
  })
})
