import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { VoDetailPanel } from './VoDetailPanel'
import type { VoDetailPanelProps } from './VoDetailPanel'
import type { VariationOrderDto } from '../types'

function makeVo(overrides: Partial<VariationOrderDto> = {}): VariationOrderDto {
  return {
    id: 'vo-1',
    projectId: 'project-1',
    voNumber: 'VO-018',
    description: 'งานเพิ่มกันสาดอลูมิเนียมทางเข้าหลัก',
    justification: 'เจ้าของโครงการร้องขอ',
    amount: '2400000.00',
    type: 'Add',
    timeImpactDays: 0,
    status: 'Draft',
    revisionNo: 1,
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
    ...overrides,
  }
}

function baseProps(overrides: Partial<VoDetailPanelProps> = {}): VoDetailPanelProps {
  return {
    vo: makeVo(),
    currentBac: 485_000_000,
    currentContractValue: 485_000_000,
    cumulativeApprovedVoBefore: 6_000_000,
    escalationBaselineContractValue: null,
    escalationThresholdPct: null,
    onOpenEdit: vi.fn(),
    canEdit: true,
    onSubmit: vi.fn(),
    submitting: false,
    canSubmit: true,
    onWithdraw: vi.fn(),
    withdrawing: false,
    canWithdraw: true,
    onOpenCancel: vi.fn(),
    canCancel: true,
    ...overrides,
  }
}

describe('VoDetailPanel', () => {
  it('empty state: prompts to select a VO when none is given', () => {
    render(<VoDetailPanel {...baseProps({ vo: null })} />)
    expect(screen.getByText(/เลือก Variation Order ทางด้านซ้าย/)).toBeInTheDocument()
  })

  it('Draft: shows ส่งอนุมัติ / แก้ไข / ยกเลิก, never ถอนคำขอ', () => {
    render(<VoDetailPanel {...baseProps()} />)

    expect(screen.getByRole('button', { name: 'ส่งอนุมัติ' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'แก้ไข' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'ยกเลิก' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'ถอนคำขอ' })).not.toBeInTheDocument()
  })

  it('PendingApproval: shows ถอนคำขอ only, never ส่งอนุมัติ/แก้ไข/ยกเลิก', () => {
    render(<VoDetailPanel {...baseProps({ vo: makeVo({ status: 'PendingApproval', currentStepNo: 1, totalSteps: 2 }) })} />)

    expect(screen.getByRole('button', { name: 'ถอนคำขอ' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'ส่งอนุมัติ' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'แก้ไข' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'ยกเลิก' })).not.toBeInTheDocument()
  })

  it('shows the live BacImpactPanel preview (signed amount, never abs()) while Draft/PendingApproval', () => {
    render(<VoDetailPanel {...baseProps()} />)
    expect(screen.getByText('ผลกระทบต่อ BAC / มูลค่าสัญญา (ก่อนยืนยัน)')).toBeInTheDocument()
    // "+2,400,000.00 บาท" legitimately appears twice — the panel's own "มูลค่า VO" row and the
    // embedded BacImpactPanel's "มูลค่า VO นี้ (Amount)" row both show the VO's signed amount.
    expect(screen.getAllByText('+2,400,000.00 บาท').length).toBe(2)
  })

  it('degrades honestly (no fabricated 0-based preview) when currentBac/currentContractValue are unknown', () => {
    render(<VoDetailPanel {...baseProps({ currentBac: null, currentContractValue: null })} />)

    expect(screen.queryByText('ผลกระทบต่อ BAC / มูลค่าสัญญา (ก่อนยืนยัน)')).not.toBeInTheDocument()
    expect(screen.getByText(/ยังไม่สามารถแสดงผลกระทบต่อ BAC/)).toBeInTheDocument()
  })

  it('Approved: renders the actual recorded before/after figures, not a live preview, and no lifecycle buttons', () => {
    const approved = makeVo({
      status: 'Approved',
      approvedAt: '2026-08-11T00:00:00+07:00',
      bacBefore: '485000000.00',
      bacAfter: '487400000.00',
      contractValueBefore: '485000000.00',
      contractValueAfter: '487400000.00',
      cumulativeVoPctAtApproval: '1.73',
    })
    render(<VoDetailPanel {...baseProps({ vo: approved })} />)

    expect(screen.getByText('ผลกระทบที่บันทึกไว้ตอนอนุมัติ')).toBeInTheDocument()
    // BAC and ContractValue read identically in this fixture (both before/after pairs match) — the
    // "BAC เดิม → ใหม่" and "มูลค่าสัญญาเดิม → ใหม่" rows both render it.
    expect(screen.getAllByText('485,000,000.00 → 487,400,000.00 บาท')).toHaveLength(2)
    expect(screen.getByText('1.73%')).toBeInTheDocument()
    expect(screen.queryByText('ผลกระทบต่อ BAC / มูลค่าสัญญา (ก่อนยืนยัน)')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'ส่งอนุมัติ' })).not.toBeInTheDocument()
  })

  it('a returned-for-revision VO (revisionNo > 1) shows a note that it was previously returned', () => {
    render(<VoDetailPanel {...baseProps({ vo: makeVo({ revisionNo: 2 }) })} />)
    expect(screen.getByText(/เคยถูกตีกลับมาแล้ว/)).toBeInTheDocument()
  })
})
