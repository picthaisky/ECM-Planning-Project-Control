import { afterAll, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { VoPage } from './VoPage'
import * as voApi from './api'
import * as infoApi from '../info/api'
import { useAuthStore } from '../../store/authStore'
import type { UserRole } from '../../store/authStore'
import type { Project } from '../info'
import type { VariationOrderDto } from './types'

// jsdom performs no real layout, so the scroll container's offsetWidth/Height (what
// @tanstack/react-virtual reads to size the `VoTable` viewport) default to 0 — same stub as
// `features/payment/PaymentPage.test.tsx`/`components/DataTable.test.tsx`.
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
    listVariationOrders: vi.fn(),
    getVoApprovalActions: vi.fn(),
    submitVariationOrder: vi.fn(),
    approveVariationOrder: vi.fn(),
    returnVariationOrderForRevision: vi.fn(),
    rejectVariationOrder: vi.fn(),
    withdrawVariationOrder: vi.fn(),
    cancelVariationOrder: vi.fn(),
    createVariationOrder: vi.fn(),
    updateVariationOrderContent: vi.fn(),
  }
})

vi.mock('../info/api', async () => {
  const actual = await vi.importActual<typeof import('../info/api')>('../info/api')
  return { ...actual, getProject: vi.fn(), updateProject: vi.fn() }
})

const draftVo: VariationOrderDto = {
  id: 'vo-1',
  projectId: 'project-1',
  voNumber: 'VO-018',
  description: 'งานเพิ่มกันสาดอลูมิเนียมทางเข้าหลัก',
  justification: null,
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
    <MemoryRouter initialEntries={['/app/project-1/vo']}>
      <Routes>
        <Route path="/app/:projectId/vo" element={<VoPage />} />
      </Routes>
    </MemoryRouter>,
  )
}

describe('VoPage', () => {
  beforeEach(() => {
    useAuthStore.getState().logout()
    vi.mocked(voApi.listVariationOrders).mockReset()
    vi.mocked(voApi.getVoApprovalActions).mockReset()
    vi.mocked(voApi.submitVariationOrder).mockReset()
    vi.mocked(voApi.approveVariationOrder).mockReset()
    vi.mocked(infoApi.getProject).mockReset()

    vi.mocked(voApi.getVoApprovalActions).mockRejectedValue(new voApi.VoApiError('ไม่พบข้อมูล', 404))
    vi.mocked(infoApi.getProject).mockResolvedValue(project)
  })

  it('loads the VO register, auto-selects the first row, and renders its detail panel', async () => {
    vi.mocked(voApi.listVariationOrders).mockResolvedValueOnce([draftVo])

    renderPage()

    await waitFor(() => expect(screen.getAllByText('VO-018').length).toBeGreaterThan(0))
    // Appears in both the detail panel's own "มูลค่า VO" row and the embedded BacImpactPanel's
    // "มูลค่า VO นี้ (Amount)" row.
    expect(screen.getAllByText('+2,400,000.00 บาท').length).toBeGreaterThan(0)
  })

  it('empty state: shows the Thai empty message and the "select a VO" prompt', async () => {
    vi.mocked(voApi.listVariationOrders).mockResolvedValueOnce([])

    renderPage()

    await waitFor(() => expect(screen.getByText('ยังไม่มี Variation Order ในโครงการนี้')).toBeInTheDocument())
    expect(screen.getByText(/เลือก Variation Order ทางด้านซ้าย/)).toBeInTheDocument()
  })

  it('a PM sees + สร้าง Variation Order; a Site engineer does not', async () => {
    vi.mocked(voApi.listVariationOrders).mockResolvedValue([draftVo])

    const { unmount } = renderPage('PM')
    await waitFor(() => expect(screen.getByRole('button', { name: '+ สร้าง Variation Order' })).toBeInTheDocument())
    unmount()

    renderPage('Site')
    await waitFor(() => expect(screen.getAllByText('VO-018').length).toBeGreaterThan(0))
    expect(screen.queryByRole('button', { name: '+ สร้าง Variation Order' })).not.toBeInTheDocument()
  })

  it('submitting calls the real submit endpoint and the chain bar reflects the response without a second fetch', async () => {
    vi.mocked(voApi.listVariationOrders).mockResolvedValueOnce([draftVo])
    vi.mocked(voApi.submitVariationOrder).mockResolvedValueOnce({
      ...draftVo,
      status: 'PendingApproval',
      currentStepNo: 1,
      totalSteps: 2,
      approvalSteps: [
        { stepNo: 1, requiredRole: 'PM', quorumCount: 1 },
        { stepNo: 2, requiredRole: 'ProjectDirector', quorumCount: 1 },
      ],
      submittedByUserId: 'user-1',
      submittedAt: '2026-08-10T09:00:00+07:00',
    })

    renderPage('PM')
    await waitFor(() => expect(screen.getByRole('button', { name: 'ส่งอนุมัติ' })).toBeInTheDocument())

    await userEvent.click(screen.getByRole('button', { name: 'ส่งอนุมัติ' }))

    expect(voApi.submitVariationOrder).toHaveBeenCalledWith('vo-1')
    await waitFor(() => expect(screen.getByText('ขั้นตอนที่ 1 จาก 2')).toBeInTheDocument())
    expect(voApi.listVariationOrders).toHaveBeenCalledTimes(1)
  })

  it('the "ตีกลับ" action button maps to ReturnForRevision, and the returned VO becomes editable/resubmittable again', async () => {
    const pending: VariationOrderDto = {
      ...draftVo,
      status: 'PendingApproval',
      currentStepNo: 1,
      totalSteps: 1,
      approvalSteps: [{ stepNo: 1, requiredRole: 'PM', quorumCount: 1 }],
      submittedByUserId: 'someone-else',
      submittedAt: '2026-08-10T09:00:00+07:00',
    }
    vi.mocked(voApi.listVariationOrders).mockResolvedValueOnce([pending])
    vi.mocked(voApi.returnVariationOrderForRevision).mockResolvedValueOnce({
      ...pending,
      status: 'Draft',
      revisionNo: 2,
      currentStepNo: 0,
      totalSteps: 0,
      approvalSteps: [],
    })

    renderPage('PM')
    await waitFor(() => expect(screen.getByRole('button', { name: 'ตีกลับแก้ไข' })).toBeInTheDocument())

    await userEvent.click(screen.getByRole('button', { name: 'ตีกลับแก้ไข' }))
    const dialog = await screen.findByRole('dialog')
    await userEvent.type(screen.getByPlaceholderText('เหตุผลที่ตีกลับ (บังคับกรอก)'), 'ยอดไม่ตรงกับ BOQ')
    await userEvent.click(screen.getByRole('button', { name: 'ยืนยันตีกลับ' }))
    void dialog

    expect(voApi.returnVariationOrderForRevision).toHaveBeenCalledWith('vo-1', 'ยอดไม่ตรงกับ BOQ')
    // Back in Draft — the resubmission path (แก้ไข/ส่งอนุมัติ) is available again, never a
    // "Rejected"-flavoured terminal status.
    await waitFor(() => expect(screen.getByRole('button', { name: 'ส่งอนุมัติ' })).toBeInTheDocument())
    expect(screen.getByRole('button', { name: 'แก้ไข' })).toBeInTheDocument()
    expect(screen.queryByText('ปฏิเสธ')).not.toBeInTheDocument()
  })

  it('shows an escalation explanation when the final step is Executive', async () => {
    const escalated: VariationOrderDto = {
      ...draftVo,
      amount: '3200000.00',
      status: 'PendingApproval',
      currentStepNo: 1,
      totalSteps: 3,
      approvalSteps: [
        { stepNo: 1, requiredRole: 'PM', quorumCount: 1 },
        { stepNo: 2, requiredRole: 'ProjectDirector', quorumCount: 1 },
        { stepNo: 3, requiredRole: 'Executive', quorumCount: 1 },
      ],
      submittedByUserId: 'someone-else',
      submittedAt: '2026-08-10T09:00:00+07:00',
    }
    vi.mocked(voApi.listVariationOrders).mockResolvedValueOnce([escalated])

    renderPage('PM')

    await waitFor(() => expect(screen.getByText(/ขั้นตอนอนุมัติสุดท้ายกำหนดให้ Executive อนุมัติ/)).toBeInTheDocument())
  })

  it('shows the REAL vote count (not just "quorum not met") once history is available — the voteProgress.ts path', async () => {
    // A QuorumCount=2 step whose first approval leaves status/currentStepNo unchanged. Unlike every
    // other test in this file, the history endpoint here resolves successfully (simulating the
    // `GetApprovalActionHistoryQueryHandler` gap being fixed) — `VoPage` must prefer the precise
    // "1/2" count derived from that real history over the generic "Quorum ยังไม่ครบ" wording.
    const dualControl: VariationOrderDto = {
      ...draftVo,
      status: 'PendingApproval',
      currentStepNo: 1,
      totalSteps: 1,
      approvalSteps: [{ stepNo: 1, requiredRole: 'ProjectDirector', quorumCount: 2 }],
      // Neither creator nor submitter is the logged-in test user ('user-1') — otherwise the
      // (correct) self-approval guard would hide "อนุมัติ" entirely, which is not what this test is
      // about.
      createdByUserId: 'someone-else',
      submittedByUserId: 'someone-else',
      submittedAt: '2026-08-10T09:00:00+07:00',
    }
    vi.mocked(voApi.listVariationOrders).mockResolvedValueOnce([dualControl])
    vi.mocked(voApi.getVoApprovalActions).mockResolvedValue([
      {
        id: 'action-1',
        documentType: 'VariationOrder',
        documentId: 'vo-1',
        revisionNo: 1,
        stepNo: 1,
        actorUserId: 'pd-a',
        actorRoleAtTime: 'ProjectDirector',
        action: 'Approve',
        comment: null,
        actedAt: '2026-08-10T09:05:00+07:00',
        approvalPolicyId: 'policy-1',
        approvalPolicyVersion: 1,
      },
    ])
    // The response is unchanged from the pre-call VO (status/currentStepNo identical) — the
    // before/after diff that drives `quorumPendingNotice` in the first place.
    vi.mocked(voApi.approveVariationOrder).mockResolvedValueOnce(dualControl)

    renderPage('PM')
    await waitFor(() => expect(screen.getByRole('button', { name: 'อนุมัติ' })).toBeInTheDocument())

    await userEvent.click(screen.getByRole('button', { name: 'อนุมัติ' }))
    const dialog = await screen.findByRole('dialog')
    await userEvent.click(within(dialog).getByRole('button', { name: 'อนุมัติ' }))

    const notice = await screen.findByRole('status')
    expect(notice).toHaveTextContent('1/2 เสียง')
  })
})
