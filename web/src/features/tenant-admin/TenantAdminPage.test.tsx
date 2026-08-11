import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { TenantAdminPage } from './TenantAdminPage'
import * as api from './api'
import { useAuthStore } from '../../store/authStore'
import type { ApprovalPolicy } from './types'

vi.mock('./api', async () => {
  const actual = await vi.importActual<typeof import('./api')>('./api')
  return { ...actual, getApprovalPolicy: vi.fn(), updateApprovalPolicy: vi.fn() }
})

const paymentPolicy: ApprovalPolicy = {
  documentType: 'PaymentCertificate',
  version: 2,
  isActive: true,
  allowSelfApproval: false,
  cumulativeVoEscalationPct: null,
  cumulativeVoEscalationRole: null,
  rules: [{ stepNo: 1, minAmount: '0.00', maxAmount: null, requiredRole: 'QS', quorumCount: 1 }],
}

const voPolicy: ApprovalPolicy = {
  documentType: 'VariationOrder',
  version: 5,
  isActive: true,
  allowSelfApproval: false,
  cumulativeVoEscalationPct: '10.00',
  cumulativeVoEscalationRole: 'Executive',
  rules: [{ stepNo: 1, minAmount: '0.00', maxAmount: null, requiredRole: 'PM', quorumCount: 1 }],
}

function renderPage() {
  useAuthStore.getState().login({
    accessToken: 'jwt',
    expiresAt: '2027-01-01T00:00:00+07:00',
    userId: 'admin-1',
    tenantId: 'tenant-1',
    role: 'Admin',
  })

  return render(
    <MemoryRouter>
      <TenantAdminPage />
    </MemoryRouter>,
  )
}

describe('TenantAdminPage', () => {
  beforeEach(() => {
    useAuthStore.getState().logout()
    vi.mocked(api.getApprovalPolicy).mockReset()
    vi.mocked(api.updateApprovalPolicy).mockReset()
  })

  it('renders the tenant-level header (not the project AppShell) with a way back to the project area', async () => {
    vi.mocked(api.getApprovalPolicy).mockResolvedValue(paymentPolicy)
    renderPage()

    expect(screen.getByText('Tenant Admin — นโยบายการอนุมัติ (Approval Policy)')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'กลับสู่โครงการ' })).toHaveAttribute('href', '/')
    await waitFor(() => expect(api.getApprovalPolicy).toHaveBeenCalledWith('tenant-1', 'PaymentCertificate'))
  })

  it('defaults to the PaymentCertificate tab and loads that policy', async () => {
    vi.mocked(api.getApprovalPolicy).mockResolvedValueOnce(paymentPolicy)
    renderPage()

    await waitFor(() => expect(screen.getByText('v2')).toBeInTheDocument())
    expect(screen.getByRole('tab', { name: 'Payment Certificate' })).toHaveAttribute('aria-selected', 'true')
  })

  it('switching to the VariationOrder tab loads that document type\'s policy and shows its VO-only escalation fields', async () => {
    vi.mocked(api.getApprovalPolicy).mockResolvedValueOnce(paymentPolicy).mockResolvedValueOnce(voPolicy)
    renderPage()

    await waitFor(() => expect(screen.getByText('v2')).toBeInTheDocument())

    await userEvent.click(screen.getByRole('tab', { name: 'Variation Order' }))

    await waitFor(() => expect(api.getApprovalPolicy).toHaveBeenCalledWith('tenant-1', 'VariationOrder'))
    await waitFor(() => expect(screen.getByText('v5')).toBeInTheDocument())
    expect(screen.getByText('Cumulative VO Escalation (%)')).toBeInTheDocument()
    expect(screen.getByDisplayValue('10.00')).toBeInTheDocument()
  })

  it('a "saved" banner from one document type does not leak into the other after switching tabs', async () => {
    vi.mocked(api.getApprovalPolicy).mockResolvedValue(paymentPolicy)
    vi.mocked(api.updateApprovalPolicy).mockResolvedValueOnce({ ...paymentPolicy, version: 3 })
    renderPage()

    await waitFor(() => expect(screen.getByText('v2')).toBeInTheDocument())
    await userEvent.click(screen.getByRole('button', { name: 'บันทึกนโยบาย' }))
    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent('v3'))

    vi.mocked(api.getApprovalPolicy).mockResolvedValueOnce(voPolicy)
    await userEvent.click(screen.getByRole('tab', { name: 'Variation Order' }))

    await waitFor(() => expect(screen.getByText('v5')).toBeInTheDocument())
    expect(screen.queryByRole('status')).not.toBeInTheDocument()
  })
})
