import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { ProjectEditForm } from './ProjectEditForm'
import type { Project } from './types'

const baseProject: Project = {
  id: 'project-1',
  name: 'Riverside Condominium Tower B',
  code: 'RCT-B',
  owner: 'Siam Riverside Development PLC',
  contractStart: '2025-10-01T00:00:00+07:00',
  contractFinish: '2027-03-31T00:00:00+07:00',
  bac: '850000000.00',
  contractValue: '850000000.00',
  retentionRate: '5.00',
  advanceRate: '10.00',
  retentionCapPercentage: null,
  retentionRelease1Percentage: '50.00',
  defectsLiabilityMonths: 24,
  advanceAmountPaid: null,
  advanceRecoveryMethod: 'ProRata',
  advanceRecoveryStartPct: null,
  advanceRecoveryRatePct: null,
  advanceRecoveryEndPct: null,
}

describe('ProjectEditForm', () => {
  it('pre-fills every field from the given project', () => {
    render(
      <ProjectEditForm project={baseProject} saving={false} serverError={null} onCancel={vi.fn()} onSubmit={vi.fn()} />,
    )

    expect(screen.getByLabelText('ชื่อโครงการ')).toHaveValue('Riverside Condominium Tower B')
    expect(screen.getByLabelText('รหัสโครงการ')).toHaveValue('RCT-B')
    expect(screen.getByLabelText('Retention Rate (%)')).toHaveValue(5)
    expect(screen.getByLabelText('ไม่มีเพดาน Retention (มาตรฐานไทย)')).toBeChecked()
  })

  it('rejects ContractFinish before ContractStart with the Thai message and never calls onSubmit', async () => {
    const onSubmit = vi.fn()
    const user = userEvent.setup()
    render(
      <ProjectEditForm project={baseProject} saving={false} serverError={null} onCancel={vi.fn()} onSubmit={onSubmit} />,
    )

    const finish = screen.getByLabelText('วันสิ้นสุดสัญญา')
    await user.clear(finish)
    await user.type(finish, '2024-01-01')
    await user.click(screen.getByRole('button', { name: 'บันทึก' }))

    expect(await screen.findByText('วันที่สิ้นสุดสัญญาต้องไม่ก่อนวันที่เริ่มสัญญา')).toBeInTheDocument()
    expect(onSubmit).not.toHaveBeenCalled()
  })

  it('unchecking "uncapped" reveals the Retention Cap % field and validates it 0-100', async () => {
    const onSubmit = vi.fn()
    const user = userEvent.setup()
    render(
      <ProjectEditForm project={baseProject} saving={false} serverError={null} onCancel={vi.fn()} onSubmit={onSubmit} />,
    )

    await user.click(screen.getByLabelText('ไม่มีเพดาน Retention (มาตรฐานไทย)'))
    const capInput = screen.getByLabelText('Retention Cap (% ของมูลค่าสัญญา)')
    await user.type(capInput, '-5')
    await user.click(screen.getByRole('button', { name: 'บันทึก' }))

    expect(await screen.findByText('ต้องอยู่ระหว่าง 0.00 ถึง 100.00')).toBeInTheDocument()
    expect(onSubmit).not.toHaveBeenCalled()
  })

  it('shows the ThresholdBanded fields only when that recovery method is selected, and submits a well-formed payload', async () => {
    const onSubmit = vi.fn()
    const user = userEvent.setup()
    render(
      <ProjectEditForm project={baseProject} saving={false} serverError={null} onCancel={vi.fn()} onSubmit={onSubmit} />,
    )

    expect(screen.queryByLabelText('เริ่มหักคืนเมื่อสะสมถึง (%)')).not.toBeInTheDocument()

    await user.selectOptions(screen.getByLabelText('วิธีหักคืนเงินล่วงหน้า'), 'ThresholdBanded')
    expect(screen.getByLabelText('เริ่มหักคืนเมื่อสะสมถึง (%)')).toBeInTheDocument()

    await user.type(screen.getByLabelText('เริ่มหักคืนเมื่อสะสมถึง (%)'), '10')
    await user.type(screen.getByLabelText('อัตราหักคืนส่วนเกิน (%)'), '25')
    await user.type(screen.getByLabelText('บังคับหักคืนครบเมื่อสะสมถึง (%)'), '90')
    await user.click(screen.getByRole('button', { name: 'บันทึก' }))

    expect(onSubmit).toHaveBeenCalledTimes(1)
    const payload = onSubmit.mock.calls[0][0]
    expect(payload.advanceRecoveryMethod).toBe('ThresholdBanded')
    expect(payload.advanceRecoveryStartPct).toBe('10')
    expect(payload.advanceRecoveryRatePct).toBe('25')
    expect(payload.advanceRecoveryEndPct).toBe('90')
    expect(payload.retentionCapPercentage).toBeNull()
  })

  it('shows the server error and disables the buttons while saving', () => {
    render(
      <ProjectEditForm
        project={baseProject}
        saving
        serverError="บันทึกไม่สำเร็จ"
        onCancel={vi.fn()}
        onSubmit={vi.fn()}
      />,
    )

    expect(screen.getByText('บันทึกไม่สำเร็จ')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'บันทึก' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'ยกเลิก' })).toBeDisabled()
  })
})
