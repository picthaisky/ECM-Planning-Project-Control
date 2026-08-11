import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { VoContentModal } from './VoContentModal'
import type { VariationOrderDto } from '../types'

const baseVo: VariationOrderDto = {
  id: 'vo-1',
  projectId: 'project-1',
  voNumber: 'VO-018',
  description: 'งานเดิม',
  justification: null,
  amount: '2400000.00',
  type: 'Add',
  timeImpactDays: 0,
  status: 'Draft',
  revisionNo: 2,
  currentStepNo: 0,
  totalSteps: 0,
  approvalPolicyId: null,
  approvalPolicyVersion: null,
  allowSelfApproval: false,
  approvalSteps: [],
  scopeItems: [{ activityId: 'activity-1', budgetCostDelta: '2400000.00', note: null }],
  createdByUserId: 'user-1',
  submittedByUserId: null,
  submittedAt: null,
  approvedAt: null,
  bacBefore: null,
  bacAfter: null,
  contractValueBefore: null,
  contractValueAfter: null,
  cumulativeVoPctAtApproval: null,
  escalationBasisContractValue: null,
}

describe('VoContentModal', () => {
  it('create mode: submits a balanced VO with the entered VoNumber/amount/scope', async () => {
    const onCreate = vi.fn()
    render(
      <VoContentModal mode="create" isOpen onClose={vi.fn()} busy={false} errorMessage={null} onCreate={onCreate} onUpdate={vi.fn()} />,
    )

    await userEvent.type(screen.getByLabelText('เลขที่ VO'), 'VO-020')
    await userEvent.type(screen.getByLabelText(/มูลค่า VO/), '500000')
    await userEvent.type(screen.getByPlaceholderText('Activity ID'), 'activity-9')
    await userEvent.type(screen.getByPlaceholderText('งบที่ปรับ (บาท)'), '500000')

    await userEvent.click(screen.getByRole('button', { name: 'บันทึก' }))

    expect(onCreate).toHaveBeenCalledWith(
      expect.objectContaining({
        voNumber: 'VO-020',
        amount: '500000',
        scopeItems: [{ activityId: 'activity-9', budgetCostDelta: '500000', note: null }],
      }),
    )
  })

  it('blocks submit and shows the mismatch reason when Scope total does not equal Amount', async () => {
    const onCreate = vi.fn()
    render(
      <VoContentModal mode="create" isOpen onClose={vi.fn()} busy={false} errorMessage={null} onCreate={onCreate} onUpdate={vi.fn()} />,
    )

    await userEvent.type(screen.getByLabelText('เลขที่ VO'), 'VO-021')
    await userEvent.type(screen.getByLabelText(/มูลค่า VO/), '500000')
    await userEvent.type(screen.getByPlaceholderText('Activity ID'), 'activity-9')
    await userEvent.type(screen.getByPlaceholderText('งบที่ปรับ (บาท)'), '400000')

    await userEvent.click(screen.getByRole('button', { name: 'บันทึก' }))

    expect(onCreate).not.toHaveBeenCalled()
    expect(screen.getByText(/ต้องเท่ากับมูลค่า VO/)).toBeInTheDocument()
  })

  it('blocks the empty-variation case (zero amount, zero time impact, no scope)', async () => {
    const onCreate = vi.fn()
    render(
      <VoContentModal mode="create" isOpen onClose={vi.fn()} busy={false} errorMessage={null} onCreate={onCreate} onUpdate={vi.fn()} />,
    )

    await userEvent.type(screen.getByLabelText('เลขที่ VO'), 'VO-022')
    await userEvent.type(screen.getByLabelText(/มูลค่า VO/), '0')

    await userEvent.click(screen.getByRole('button', { name: 'บันทึก' }))

    expect(onCreate).not.toHaveBeenCalled()
    expect(screen.getByText(/ต้องระบุมูลค่า, ผลกระทบต่อระยะเวลา หรือรายการ Scope/)).toBeInTheDocument()
  })

  it('edit mode: prefills from the VO, keeps VoNumber immutable, and calls onUpdate with the id', async () => {
    const onUpdate = vi.fn()
    render(
      <VoContentModal mode="edit" isOpen onClose={vi.fn()} initialVo={baseVo} busy={false} errorMessage={null} onCreate={vi.fn()} onUpdate={onUpdate} />,
    )

    const voNumberInput = screen.getByLabelText('เลขที่ VO') as HTMLInputElement
    expect(voNumberInput).toBeDisabled()
    expect(voNumberInput).toHaveValue('VO-018')
    expect(screen.getByDisplayValue('activity-1')).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'บันทึก' }))

    expect(onUpdate).toHaveBeenCalledWith(
      'vo-1',
      expect.objectContaining({ amount: '2400000.00', scopeItems: [{ activityId: 'activity-1', budgetCostDelta: '2400000.00', note: null }] }),
    )
  })

  it('shows the server error message when supplied', () => {
    render(
      <VoContentModal
        mode="create"
        isOpen
        onClose={vi.fn()}
        busy={false}
        errorMessage="เลขที่ VO นี้ถูกใช้แล้วในโครงการนี้ กรุณาระบุเลขที่อื่น"
        onCreate={vi.fn()}
        onUpdate={vi.fn()}
      />,
    )

    expect(screen.getByText('เลขที่ VO นี้ถูกใช้แล้วในโครงการนี้ กรุณาระบุเลขที่อื่น')).toBeInTheDocument()
  })
})
