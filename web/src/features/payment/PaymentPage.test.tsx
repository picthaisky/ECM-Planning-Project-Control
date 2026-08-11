import { afterAll, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { PaymentPage } from './PaymentPage'
import * as paymentApi from './api'
import * as infoApi from '../info/api'
import { useAuthStore } from '../../store/authStore'
import type { UserRole } from '../../store/authStore'
import type { Project } from '../info'
import type { PaymentCertificateDto } from './types'

// jsdom performs no real layout, so the scroll container's offsetWidth/Height (what
// @tanstack/react-virtual reads to size the `MilestoneCertificateTable` viewport) default to 0 —
// same stub as `components/DataTable.test.tsx`/`features/wbs/WbsPage.test.tsx`.
beforeAll(() => {
  Object.defineProperty(HTMLElement.prototype, 'offsetHeight', { configurable: true, get: () => 420 })
  Object.defineProperty(HTMLElement.prototype, 'offsetWidth', { configurable: true, get: () => 800 })
})

afterAll(() => {
  Reflect.deleteProperty(HTMLElement.prototype, 'offsetHeight')
  Reflect.deleteProperty(HTMLElement.prototype, 'offsetWidth')
})

vi.mock('./api', async () => {
  const actual = await vi.importActual<typeof import('./api')>('./api')
  return {
    ...actual,
    listPaymentCertificates: vi.fn(),
    getApprovalActions: vi.fn(),
    submitPaymentCertificate: vi.fn(),
    approvePaymentCertificate: vi.fn(),
    returnPaymentCertificateForRevision: vi.fn(),
    rejectPaymentCertificate: vi.fn(),
    recordPayment: vi.fn(),
  }
})

vi.mock('../info/api', async () => {
  const actual = await vi.importActual<typeof import('../info/api')>('../info/api')
  return { ...actual, getProject: vi.fn(), updateProject: vi.fn() }
})

const certificate: PaymentCertificateDto = {
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
  createdByUserId: 'user-9',
  submittedByUserId: null,
  submittedAt: null,
  certifiedAt: null,
  paidAt: null,
  paymentReference: null,
}

const project: Project = {
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
}

function renderPage(role: UserRole = 'PM') {
  useAuthStore.getState().login({
    accessToken: 'jwt',
    expiresAt: '2027-01-01T00:00:00+07:00',
    userId: 'user-1',
    tenantId: 'tenant-1',
    role,
  })

  return render(
    <MemoryRouter initialEntries={['/app/project-1/payment']}>
      <Routes>
        <Route path="/app/:projectId/payment" element={<PaymentPage />} />
      </Routes>
    </MemoryRouter>,
  )
}

describe('PaymentPage', () => {
  beforeEach(() => {
    useAuthStore.getState().logout()
    vi.mocked(paymentApi.listPaymentCertificates).mockReset()
    vi.mocked(paymentApi.getApprovalActions).mockReset()
    vi.mocked(paymentApi.submitPaymentCertificate).mockReset()
    vi.mocked(infoApi.getProject).mockReset()
    vi.mocked(infoApi.updateProject).mockReset()

    vi.mocked(paymentApi.getApprovalActions).mockRejectedValue(new paymentApi.PaymentApiError('ไม่พบข้อมูล', 404))
    vi.mocked(infoApi.getProject).mockResolvedValue(project)
  })

  it('loads the milestone list, auto-selects the first certificate, and renders its breakdown', async () => {
    vi.mocked(paymentApi.listPaymentCertificates).mockResolvedValueOnce([certificate])

    renderPage()

    // The milestone appears both as a list row (left) and in the auto-selected detail panel
    // (right) — assert both render, rather than a single ambiguous text query.
    await waitFor(() => expect(screen.getAllByText('งานโครงสร้างชั้น 3-5')).toHaveLength(2))
    // The certificate detail panel (right column) shows the Gross/Retention/Advance/Net breakdown
    // for the auto-selected certificate.
    expect(screen.getByText('Payment Certificate — งวดที่ 3')).toBeInTheDocument()
    expect(screen.getByText('−2,160,000.00 บาท')).toBeInTheDocument() // advance recovery line
  })

  it('a PM/QS/ProjectDirector/Admin sees the ส่งอนุมัติ button for a Draft certificate; a Site engineer does not', async () => {
    vi.mocked(paymentApi.listPaymentCertificates).mockResolvedValue([certificate])

    const { unmount } = renderPage('PM')
    await waitFor(() => expect(screen.getByRole('button', { name: 'ส่งอนุมัติ' })).toBeInTheDocument())
    unmount()

    renderPage('Site')
    await waitFor(() => expect(screen.getByText('Payment Certificate — งวดที่ 3')).toBeInTheDocument())
    expect(screen.queryByRole('button', { name: 'ส่งอนุมัติ' })).not.toBeInTheDocument()
  })

  it('submitting calls the real submit endpoint and the panel reflects the response without a second fetch', async () => {
    vi.mocked(paymentApi.listPaymentCertificates).mockResolvedValueOnce([certificate])
    vi.mocked(paymentApi.submitPaymentCertificate).mockResolvedValueOnce({
      ...certificate,
      status: 'PendingApproval',
      currentStepNo: 1,
      totalSteps: 2,
      submittedByUserId: 'user-1',
      submittedAt: '2026-08-09T00:00:00+07:00',
    })

    renderPage('PM')
    await waitFor(() => expect(screen.getByRole('button', { name: 'ส่งอนุมัติ' })).toBeInTheDocument())

    await userEvent.click(screen.getByRole('button', { name: 'ส่งอนุมัติ' }))

    expect(paymentApi.submitPaymentCertificate).toHaveBeenCalledWith('cert-1')
    await waitFor(() => expect(screen.getByText('ขั้นตอนที่ 1 จาก 2')).toBeInTheDocument())
    // listPaymentCertificates (the not-yet-real list endpoint) is called exactly once — the
    // interactive flow relies on the mutation's own response, never a re-fetch.
    expect(paymentApi.listPaymentCertificates).toHaveBeenCalledTimes(1)
  })

  it('the settings button opens the same ProjectEditForm used by Project Info, pre-filled with the live rates', async () => {
    vi.mocked(paymentApi.listPaymentCertificates).mockResolvedValueOnce([certificate])

    renderPage('PM')
    await waitFor(() => expect(screen.getByText('Payment Certificate — งวดที่ 3')).toBeInTheDocument())

    await userEvent.click(screen.getByRole('button', { name: /ตั้งค่า Retention/ }))

    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByRole('form', { name: 'แก้ไขข้อมูลโครงการ' })).toBeInTheDocument()
    expect(within(dialog).getByDisplayValue('5.00')).toBeInTheDocument()
  })

  it('empty state: shows the Thai empty message and no certificate panel content when the project has no certificates', async () => {
    vi.mocked(paymentApi.listPaymentCertificates).mockResolvedValueOnce([])

    renderPage()

    await waitFor(() => expect(screen.getByText('ยังไม่มีใบรับรองผลงานในโครงการนี้')).toBeInTheDocument())
    expect(screen.getByText(/เลือกงวดงานทางด้านซ้าย/)).toBeInTheDocument()
  })
})
