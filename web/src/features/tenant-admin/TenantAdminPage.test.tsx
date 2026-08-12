import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { TenantAdminPage } from './TenantAdminPage'
import * as api from './api'
import { useAuthStore } from '../../store/authStore'
import type { ApprovalPolicy, ApprovalPolicyVersionHistoryEntry, ApprovalRoutingSimulation } from './types'

vi.mock('./api', async () => {
  const actual = await vi.importActual<typeof import('./api')>('./api')
  return {
    ...actual,
    getApprovalPolicy: vi.fn(),
    updateApprovalPolicy: vi.fn(),
    getApprovalPolicyHistory: vi.fn(),
    simulateApprovalRouting: vi.fn(),
  }
})

const VALID_PROJECT_ID = '3fa85f64-5717-4562-b3fc-2c963f66afa6'

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

const voHistory: ApprovalPolicyVersionHistoryEntry[] = [
  {
    approvalPolicyId: 'policy-4',
    version: 4,
    isActive: false,
    effectiveFrom: '2026-01-01T00:00:00+07:00',
    effectiveTo: '2026-06-01T00:00:00+07:00',
    allowSelfApproval: true,
    cumulativeVoEscalationPct: null,
    cumulativeVoEscalationRole: null,
    ruleCount: 1,
    createdByUserId: 'admin-1',
    createdAt: '2026-01-01T00:00:00+07:00',
    lastModifiedByUserId: null,
    lastModifiedAt: null,
  },
  {
    approvalPolicyId: 'policy-5',
    version: 5,
    isActive: true,
    effectiveFrom: '2026-06-01T00:00:00+07:00',
    effectiveTo: null,
    allowSelfApproval: false,
    cumulativeVoEscalationPct: '10.00',
    cumulativeVoEscalationRole: 'Executive',
    ruleCount: 1,
    createdByUserId: 'admin-1',
    createdAt: '2026-06-01T00:00:00+07:00',
    lastModifiedByUserId: 'admin-1',
    lastModifiedAt: '2026-06-01T00:00:00+07:00',
  },
]

const voSimulation: ApprovalRoutingSimulation = {
  documentType: 'VariationOrder',
  projectId: VALID_PROJECT_ID,
  inputAmount: '2400000.00',
  routingAmount: '2400000.00',
  approvalPolicyId: 'policy-5',
  approvalPolicyVersion: 5,
  usedFallbackChain: false,
  steps: [{ stepNo: 1, requiredRole: 'PM', quorumCount: 1 }],
  escalationApplied: false,
  allowSelfApproval: false,
  multipleActivePoliciesDetected: false,
  ambiguousActivePolicies: [],
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
    vi.mocked(api.getApprovalPolicyHistory).mockReset()
    vi.mocked(api.simulateApprovalRouting).mockReset()
    // Sensible default so tests that don't care about history/simulator aren't forced to stub
    // them individually — overridden per-test with mockResolvedValueOnce where it matters.
    vi.mocked(api.getApprovalPolicyHistory).mockResolvedValue([])
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

  // ---------------------------------------------------------------------------------------------
  // S15-FE-01: version-history timeline + routing simulator (US-15.2).
  // ---------------------------------------------------------------------------------------------

  it('shows the version-history timeline for the active tab\'s document type, newest version first', async () => {
    vi.mocked(api.getApprovalPolicy).mockResolvedValue(voPolicy)
    // The default (PaymentCertificate) tab mounts and calls this first — branch on documentType
    // rather than mockResolvedValueOnce, which a PaymentCertificate-tab call would consume before
    // the VariationOrder tab is ever clicked.
    vi.mocked(api.getApprovalPolicyHistory).mockImplementation(async (_tenantId, documentType) =>
      documentType === 'VariationOrder' ? voHistory : [],
    )
    renderPage()

    await userEvent.click(screen.getByRole('tab', { name: 'Variation Order' }))

    const historyRegion = await screen.findByTestId('approval-policy-history')
    expect(api.getApprovalPolicyHistory).toHaveBeenCalledWith('tenant-1', 'VariationOrder')
    const items = within(historyRegion).getAllByRole('listitem')
    expect(items).toHaveLength(2)
    expect(within(items[0]).getByText('v5')).toBeInTheDocument()
    expect(within(items[0]).getByText('ใช้งานอยู่ (Active)')).toBeInTheDocument()
  })

  it('running the simulator calls the real endpoint and renders the resolved chain (the "would actually resolve" guarantee)', async () => {
    vi.mocked(api.getApprovalPolicy).mockResolvedValue(voPolicy)
    vi.mocked(api.simulateApprovalRouting).mockResolvedValueOnce(voSimulation)
    renderPage()

    await userEvent.click(screen.getByRole('tab', { name: 'Variation Order' }))
    await screen.findByText('v5')

    await userEvent.type(screen.getByLabelText('รหัสโครงการ (Project ID)'), VALID_PROJECT_ID)
    await userEvent.type(screen.getByLabelText('จำนวนเงินสมมติ (บาท)'), '2400000')
    await userEvent.click(screen.getByRole('button', { name: 'ทดลอง routing' }))

    expect(api.simulateApprovalRouting).toHaveBeenCalledWith('tenant-1', 'VariationOrder', {
      projectId: VALID_PROJECT_ID,
      amount: '2400000',
    })

    const resultRegion = await screen.findByTestId('routing-simulation-result')
    expect(within(resultRegion).getByText('v5')).toBeInTheDocument()
    expect(within(resultRegion).getByText('Project Manager')).toBeInTheDocument()
  })

  it('ADR-0021: an ambiguous simulate response renders the conflict warning, never a normal-looking chain', async () => {
    vi.mocked(api.getApprovalPolicy).mockResolvedValue(voPolicy)
    vi.mocked(api.simulateApprovalRouting).mockResolvedValueOnce({
      ...voSimulation,
      multipleActivePoliciesDetected: true,
      ambiguousActivePolicies: [
        { approvalPolicyId: 'policy-5', version: 5 },
        { approvalPolicyId: 'policy-6', version: 6 },
      ],
    })
    renderPage()

    await userEvent.click(screen.getByRole('tab', { name: 'Variation Order' }))
    await screen.findByText('v5')

    await userEvent.type(screen.getByLabelText('รหัสโครงการ (Project ID)'), VALID_PROJECT_ID)
    await userEvent.type(screen.getByLabelText('จำนวนเงินสมมติ (บาท)'), '1000000')
    await userEvent.click(screen.getByRole('button', { name: 'ทดลอง routing' }))

    const resultRegion = await screen.findByTestId('routing-simulation-result')
    expect(within(resultRegion).getByText(/ADR-0021/)).toBeInTheDocument()
    expect(within(resultRegion).getByText(/v6/)).toBeInTheDocument()
  })

  it('saving a new policy version reloads the history and clears any stale simulator result', async () => {
    vi.mocked(api.getApprovalPolicy).mockResolvedValue(paymentPolicy)
    vi.mocked(api.updateApprovalPolicy).mockResolvedValueOnce({ ...paymentPolicy, version: 3 })
    vi.mocked(api.simulateApprovalRouting).mockResolvedValueOnce({
      ...voSimulation,
      documentType: 'PaymentCertificate',
      approvalPolicyVersion: 2,
    })
    renderPage()

    await waitFor(() => expect(screen.getByText('v2')).toBeInTheDocument())

    await userEvent.type(screen.getByLabelText('รหัสโครงการ (Project ID)'), VALID_PROJECT_ID)
    await userEvent.type(screen.getByLabelText('จำนวนเงินสมมติ (บาท)'), '500000')
    await userEvent.click(screen.getByRole('button', { name: 'ทดลอง routing' }))
    await screen.findByTestId('routing-simulation-result')

    await userEvent.click(screen.getByRole('button', { name: 'บันทึกนโยบาย' }))
    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent('v3'))

    // initial load + post-save reload; and the reset() clearing the now-stale simulator result —
    // both happen after the "saved" banner appears (handleSave's own await chain), so each gets
    // its own waitFor rather than assuming they already landed by the line above.
    await waitFor(() => expect(api.getApprovalPolicyHistory).toHaveBeenCalledTimes(2))
    await waitFor(() => expect(screen.queryByTestId('routing-simulation-result')).not.toBeInTheDocument())
  })
})
