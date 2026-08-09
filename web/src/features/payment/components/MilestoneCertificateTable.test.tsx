import { afterAll, beforeAll, describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MilestoneCertificateTable } from './MilestoneCertificateTable'
import type { PaymentCertificateDto } from '../types'

// jsdom performs no real layout, so the scroll container's offsetWidth/Height (what
// @tanstack/react-virtual reads to size the viewport) default to 0, which would make every test
// render zero rows — same stub as `components/DataTable.test.tsx`'s own identical comment.
beforeAll(() => {
  Object.defineProperty(HTMLElement.prototype, 'offsetHeight', {
    configurable: true,
    get() {
      return 420
    },
  })
  Object.defineProperty(HTMLElement.prototype, 'offsetWidth', {
    configurable: true,
    get() {
      return 800
    },
  })
})

afterAll(() => {
  Reflect.deleteProperty(HTMLElement.prototype, 'offsetHeight')
  Reflect.deleteProperty(HTMLElement.prototype, 'offsetWidth')
})

function makeCertificate(overrides: Partial<PaymentCertificateDto> = {}): PaymentCertificateDto {
  return {
    id: 'cert-1',
    projectId: 'project-1',
    milestoneNo: 1,
    description: 'งานฐานราก',
    milestoneValue: '10000000.00',
    previousCumulativeApprovePct: '0.00',
    approvePct: '40.00',
    claimPct: '45.00',
    actualProgressPct: '50.00',
    grossCertifiedAmount: '4000000.00',
    retentionAmount: '200000.00',
    advanceRecoveryAmount: '400000.00',
    netPayment: '3400000.00',
    status: 'PendingApproval',
    revisionNo: 1,
    currentStepNo: 1,
    totalSteps: 2,
    approvalPolicyId: 'policy-1',
    approvalPolicyVersion: 1,
    createdByUserId: 'user-1',
    submittedByUserId: 'user-1',
    submittedAt: '2026-08-01T00:00:00+07:00',
    certifiedAt: null,
    paidAt: null,
    paymentReference: null,
    ...overrides,
  }
}

describe('MilestoneCertificateTable', () => {
  it('empty state: shows the Thai empty message when there are no certificates', () => {
    render(<MilestoneCertificateTable certificates={[]} selectedId={null} onSelect={vi.fn()} state="ready" />)
    expect(screen.getByText('ยังไม่มีใบรับรองผลงานในโครงการนี้')).toBeInTheDocument()
  })

  it('loading state', () => {
    render(<MilestoneCertificateTable certificates={[]} selectedId={null} onSelect={vi.fn()} state="loading" />)
    expect(screen.getByRole('status')).toBeInTheDocument()
  })

  it('error state shows the provided message', () => {
    render(
      <MilestoneCertificateTable
        certificates={[]}
        selectedId={null}
        onSelect={vi.fn()}
        state="error"
        errorMessage="โหลดรายการไม่สำเร็จ"
      />,
    )
    expect(screen.getByText('โหลดรายการไม่สำเร็จ')).toBeInTheDocument()
  })

  it('renders a row per certificate with the prototype-matching columns and calls onSelect on click', async () => {
    const certificate = makeCertificate()
    const onSelect = vi.fn()
    render(
      <MilestoneCertificateTable certificates={[certificate]} selectedId={null} onSelect={onSelect} state="ready" />,
    )

    expect(screen.getByText('งานฐานราก')).toBeInTheDocument()
    expect(screen.getByText('10,000,000.00')).toBeInTheDocument()
    expect(screen.getByText('50.00%')).toBeInTheDocument() // actual
    expect(screen.getByText('45.00%')).toBeInTheDocument() // claim
    expect(screen.getByText('40.00%')).toBeInTheDocument() // approve
    expect(screen.getByText('รออนุมัติ')).toBeInTheDocument() // PendingApproval status label

    await userEvent.click(screen.getByText('งานฐานราก'))
    expect(onSelect).toHaveBeenCalledWith('cert-1')
  })

  it('renders an em dash for null actual/claim percentages instead of "0.00%"', () => {
    render(
      <MilestoneCertificateTable
        certificates={[makeCertificate({ actualProgressPct: null, claimPct: null })]}
        selectedId={null}
        onSelect={vi.fn()}
        state="ready"
      />,
    )
    const dashes = screen.getAllByText('—')
    expect(dashes.length).toBeGreaterThanOrEqual(2)
  })
})
