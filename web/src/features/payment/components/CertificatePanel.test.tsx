import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { CertificatePanel } from './CertificatePanel'
import type { Project } from '../../info'
import type { PaymentCertificateDto } from '../types'

function makeCertificate(overrides: Partial<PaymentCertificateDto> = {}): PaymentCertificateDto {
  return {
    id: 'cert-1',
    projectId: 'project-1',
    milestoneNo: 3,
    description: 'งานโครงสร้างชั้น 3-5',
    milestoneValue: '21600000.00',
    previousCumulativeApprovePct: '0.00',
    approvePct: '100.00',
    claimPct: '100.00',
    actualProgressPct: '100.00',
    grossCertifiedAmount: '21600000.00',
    retentionAmount: '1080000.00',
    advanceRecoveryAmount: '2160000.00',
    netPayment: '18360000.00',
    status: 'Draft',
    revisionNo: 1,
    currentStepNo: 0,
    totalSteps: 0,
    approvalPolicyId: null,
    approvalPolicyVersion: null,
    createdByUserId: 'user-1',
    submittedByUserId: null,
    submittedAt: null,
    certifiedAt: null,
    paidAt: null,
    paymentReference: null,
    ...overrides,
  }
}

function makeProject(overrides: Partial<Project> = {}): Project {
  return {
    id: 'project-1',
    name: 'โครงการทดสอบ',
    code: 'TEST-01',
    owner: 'เจ้าของโครงการ',
    contractStart: '2025-01-01T00:00:00+07:00',
    contractFinish: '2026-12-31T00:00:00+07:00',
    bac: '485000000.00',
    contractValue: '485000000.00',
    retentionRate: '5.00',
    advanceRate: '10.00',
    retentionCapPercentage: null,
    retentionRelease1Percentage: '50.00',
    defectsLiabilityMonths: null,
    advanceAmountPaid: null,
    advanceRecoveryMethod: 'ProRata',
    advanceRecoveryStartPct: null,
    advanceRecoveryRatePct: null,
    advanceRecoveryEndPct: null,
    ...overrides,
  }
}

describe('CertificatePanel', () => {
  it('empty state: prompts to pick a milestone when nothing is selected', () => {
    render(
      <CertificatePanel
        certificate={null}
        project={null}
        onOpenSettings={vi.fn()}
        onSubmit={vi.fn()}
        submitting={false}
        canSubmit={false}
      />,
    )
    expect(screen.getByText(/เลือกงวดงานทางด้านซ้าย/)).toBeInTheDocument()
  })

  it('renders Gross -> Retention -> Advance Recovery -> Net in that order (the fixed prototype gap)', () => {
    const { container } = render(
      <CertificatePanel
        certificate={makeCertificate()}
        project={makeProject()}
        onOpenSettings={vi.fn()}
        onSubmit={vi.fn()}
        submitting={false}
        canSubmit={false}
      />,
    )

    const text = container.textContent ?? ''
    const grossIndex = text.indexOf('มูลค่าที่รับรองงวดนี้')
    const retentionIndex = text.indexOf('หัก Retention')
    const advanceIndex = text.indexOf('หัก Advance Recovery')
    const netIndex = text.indexOf('รับสุทธิ')

    expect(grossIndex).toBeGreaterThan(-1)
    expect(retentionIndex).toBeGreaterThan(grossIndex)
    expect(advanceIndex).toBeGreaterThan(retentionIndex)
    expect(netIndex).toBeGreaterThan(advanceIndex)

    // The actual backend-computed figures, never recomputed client-side.
    expect(screen.getByText('−1,080,000.00 บาท')).toBeInTheDocument()
    expect(screen.getByText('−2,160,000.00 บาท')).toBeInTheDocument()
    expect(screen.getByText('18,360,000.00 บาท')).toBeInTheDocument()
  })

  it('the advance-recovery line still renders at exactly 0.00 — never hidden', () => {
    render(
      <CertificatePanel
        certificate={makeCertificate({ advanceRecoveryAmount: '0.00' })}
        project={makeProject({ advanceRate: null })}
        onOpenSettings={vi.fn()}
        onSubmit={vi.fn()}
        submitting={false}
        canSubmit={false}
      />,
    )
    expect(screen.getByText('−0.00 บาท')).toBeInTheDocument()
    expect(screen.getByText('หัก Advance Recovery')).toBeInTheDocument()
  })

  describe('retention cap indicator', () => {
    it('shows "ไม่มีเพดาน" for the Thai-standard uncapped configuration', () => {
      render(
        <CertificatePanel
          certificate={makeCertificate()}
          project={makeProject({ retentionCapPercentage: null })}
          onOpenSettings={vi.fn()}
          onSubmit={vi.fn()}
          submitting={false}
          canSubmit={false}
        />,
      )
      expect(screen.getByText(/ไม่มีเพดาน Retention/)).toBeInTheDocument()
    })

    it('shows the cap ceiling and flags it as binding this period when actual retention is below the naive rate figure', () => {
      render(
        <CertificatePanel
          certificate={makeCertificate({
            grossCertifiedAmount: '8000000.00',
            retentionAmount: '500000.00', // capped by the backend, below the naive 10% = 800,000.00
          })}
          project={makeProject({ retentionRate: '10.00', retentionCapPercentage: '5.00', contractValue: '100000000.00' })}
          onOpenSettings={vi.fn()}
          onSubmit={vi.fn()}
          submitting={false}
          canSubmit={false}
        />,
      )
      expect(screen.getByText(/เพดาน Retention สะสมทั้งโครงการ: 5,000,000.00 บาท/)).toBeInTheDocument()
      expect(screen.getByText(/งวดนี้ถูกจำกัดโดยเพดาน/)).toBeInTheDocument()
    })

    it('renders no definitive cap indicator while the project has not loaded', () => {
      render(
        <CertificatePanel
          certificate={makeCertificate()}
          project={null}
          onOpenSettings={vi.fn()}
          onSubmit={vi.fn()}
          submitting={false}
          canSubmit={false}
        />,
      )
      expect(screen.queryByText(/ไม่มีเพดาน Retention/)).not.toBeInTheDocument()
      expect(screen.queryByText(/เพดาน Retention สะสม/)).not.toBeInTheDocument()
    })
  })

  describe('submit affordance', () => {
    it('shows "ส่งอนุมัติ" only for a Draft certificate when the role gate allows it', () => {
      render(
        <CertificatePanel
          certificate={makeCertificate({ status: 'Draft' })}
          project={makeProject()}
          onOpenSettings={vi.fn()}
          onSubmit={vi.fn()}
          submitting={false}
          canSubmit
        />,
      )
      expect(screen.getByRole('button', { name: 'ส่งอนุมัติ' })).toBeInTheDocument()
    })

    it('hides "ส่งอนุมัติ" once the certificate has already been submitted', () => {
      render(
        <CertificatePanel
          certificate={makeCertificate({ status: 'PendingApproval' })}
          project={makeProject()}
          onOpenSettings={vi.fn()}
          onSubmit={vi.fn()}
          submitting={false}
          canSubmit
        />,
      )
      expect(screen.queryByRole('button', { name: 'ส่งอนุมัติ' })).not.toBeInTheDocument()
    })

    it('hides "ส่งอนุมัติ" when the role gate disallows it, even for a Draft certificate', () => {
      render(
        <CertificatePanel
          certificate={makeCertificate({ status: 'Draft' })}
          project={makeProject()}
          onOpenSettings={vi.fn()}
          onSubmit={vi.fn()}
          submitting={false}
          canSubmit={false}
        />,
      )
      expect(screen.queryByRole('button', { name: 'ส่งอนุมัติ' })).not.toBeInTheDocument()
    })

    it('calls onSubmit when clicked', async () => {
      const onSubmit = vi.fn()
      render(
        <CertificatePanel
          certificate={makeCertificate({ status: 'Draft' })}
          project={makeProject()}
          onOpenSettings={vi.fn()}
          onSubmit={onSubmit}
          submitting={false}
          canSubmit
        />,
      )
      await userEvent.click(screen.getByRole('button', { name: 'ส่งอนุมัติ' }))
      expect(onSubmit).toHaveBeenCalledTimes(1)
    })
  })

  it('the settings button shows the configured rates and calls onOpenSettings', async () => {
    const onOpenSettings = vi.fn()
    render(
      <CertificatePanel
        certificate={makeCertificate()}
        project={makeProject({ retentionRate: '5.00', advanceRate: '10.00' })}
        onOpenSettings={onOpenSettings}
        onSubmit={vi.fn()}
        submitting={false}
        canSubmit={false}
      />,
    )

    const button = screen.getByRole('button', { name: /ตั้งค่า Retention 5\.00% \/ Advance 10\.00%/ })
    await userEvent.click(button)
    expect(onOpenSettings).toHaveBeenCalledTimes(1)
  })
})
