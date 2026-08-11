import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { EacAdvancedInputsCard } from './EacAdvancedInputsCard'
import * as api from './api'
import { useAuthStore } from '../../store/authStore'
import type { ProjectDetail, SetEacAdvancedInputsResult } from './types'

vi.mock('./api', async () => {
  const actual = await vi.importActual<typeof import('./api')>('./api')
  return { ...actual, getProject: vi.fn(), setEacAdvancedInputs: vi.fn() }
})

const sampleProject: ProjectDetail = {
  id: 'project-1',
  name: 'Riverside Condominium Tower B',
  code: 'RCT-B',
  owner: 'Siam Riverside Development PLC',
  contractStart: '2025-10-01T00:00:00+07:00',
  contractFinish: '2027-03-31T00:00:00+07:00',
  bac: '850000000.00',
  contractValue: '860000000.00',
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
  eacVariantDefault: 'CpiBased',
  eacManualEtc: null,
  eacCustomPerformanceFactor: null,
  eacManualEtcStaleSince: null,
}

function loginAs(role: 'PM' | 'QS' | 'Executive' | 'Site' | 'Admin') {
  useAuthStore.getState().login({
    accessToken: 'jwt',
    expiresAt: '2027-01-01T00:00:00+07:00',
    userId: 'user-1',
    tenantId: 'tenant-1',
    role,
  })
}

function renderCard() {
  return render(
    <MemoryRouter initialEntries={['/app/project-1/info']}>
      <Routes>
        <Route path="/app/:projectId/info" element={<EacAdvancedInputsCard projectId="project-1" />} />
      </Routes>
    </MemoryRouter>,
  )
}

describe('EacAdvancedInputsCard (S14-FE-02)', () => {
  beforeEach(() => {
    vi.mocked(api.getProject).mockReset()
    vi.mocked(api.setEacAdvancedInputs).mockReset()
    useAuthStore.getState().logout()
  })

  it('loads and prefills both fields from the current project, and shows the current default variant', async () => {
    loginAs('PM')
    vi.mocked(api.getProject).mockResolvedValueOnce({
      ...sampleProject,
      eacManualEtc: '760000.00',
      eacCustomPerformanceFactor: '1.2000',
      eacVariantDefault: 'CustomPf',
    })

    renderCard()

    await waitFor(() => expect(screen.getByLabelText(/Bottom-Up ETC/)).toHaveValue(760000))
    expect(screen.getByLabelText('Custom Performance Factor')).toHaveValue(1.2)
    expect(screen.getByText('Custom PF')).toBeInTheDocument() // current default variant label
  })

  it('a role without write access (Site) sees a read-only view, never the editable form', async () => {
    loginAs('Site')
    vi.mocked(api.getProject).mockResolvedValueOnce({
      ...sampleProject,
      eacManualEtc: '760000.00',
    })

    renderCard()

    await waitFor(() => expect(screen.getByText('760000.00')).toBeInTheDocument())
    expect(screen.queryByLabelText(/Bottom-Up ETC/)).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'บันทึก' })).not.toBeInTheDocument()
  })

  it('client-side validation rejects a negative Bottom-Up ETC / a zero-or-negative Custom PF before calling the API', async () => {
    loginAs('PM')
    vi.mocked(api.getProject).mockResolvedValueOnce(sampleProject)
    const user = userEvent.setup()

    renderCard()
    await waitFor(() => expect(screen.getByLabelText(/Bottom-Up ETC/)).toBeInTheDocument())

    await user.type(screen.getByLabelText(/Bottom-Up ETC/), '-5')
    await user.type(screen.getByLabelText('Custom Performance Factor'), '0')
    await user.click(screen.getByRole('button', { name: 'บันทึก' }))

    expect(await screen.findByText('ต้องไม่ติดลบ')).toBeInTheDocument()
    expect(screen.getByText('ต้องมากกว่า 0')).toBeInTheDocument()
    expect(api.setEacAdvancedInputs).not.toHaveBeenCalled()
  })

  it('saves both fields together (full-representation) and reflects the fresh values afterward', async () => {
    loginAs('QS')
    vi.mocked(api.getProject).mockResolvedValueOnce(sampleProject)
    const result: SetEacAdvancedInputsResult = {
      projectId: 'project-1',
      eacManualEtc: '760000.00',
      eacCustomPerformanceFactor: null,
      eacManualEtcStaleSince: null,
    }
    vi.mocked(api.setEacAdvancedInputs).mockResolvedValueOnce(result)
    const user = userEvent.setup()

    renderCard()
    await waitFor(() => expect(screen.getByLabelText(/Bottom-Up ETC/)).toBeInTheDocument())

    await user.type(screen.getByLabelText(/Bottom-Up ETC/), '760000')
    await user.click(screen.getByRole('button', { name: 'บันทึก' }))

    await waitFor(() =>
      expect(api.setEacAdvancedInputs).toHaveBeenCalledWith('project-1', {
        eacManualEtc: '760000',
        eacCustomPerformanceFactor: null,
      }),
    )
  })

  it('shows the ManualEtcPredatesBacChange staleness warning, and connects it to this exact input', async () => {
    loginAs('PM')
    vi.mocked(api.getProject).mockResolvedValueOnce({
      ...sampleProject,
      eacManualEtc: '760000.00',
      eacManualEtcStaleSince: '2026-08-01T00:00:00+07:00',
    })

    renderCard()

    await waitFor(() =>
      expect(screen.getByText(/Variation Order ที่อนุมัติแล้วเปลี่ยน BAC/)).toBeInTheDocument(),
    )
  })

  it('surfaces the "cannot clear while active" 400 in Thai, pointing back at the EVM screen', async () => {
    loginAs('PM')
    vi.mocked(api.getProject).mockResolvedValueOnce({
      ...sampleProject,
      eacManualEtc: '760000.00',
      eacVariantDefault: 'BottomUpEtc',
    })
    vi.mocked(api.setEacAdvancedInputs).mockRejectedValueOnce(
      new api.ProjectApiError(
        'ไม่สามารถล้างค่านี้ได้ เนื่องจากตัวแปร EAC เริ่มต้นของโครงการปัจจุบันคือ Bottom-Up ETC ซึ่งใช้ค่านี้อยู่ — ไปเปลี่ยนตัวแปร EAC ที่หน้า EVM S-Curve ก่อน แล้วจึงกลับมาล้างค่านี้',
        400,
      ),
    )
    const user = userEvent.setup()

    renderCard()
    await waitFor(() => expect(screen.getByLabelText(/Bottom-Up ETC/)).toHaveValue(760000))

    await user.clear(screen.getByLabelText(/Bottom-Up ETC/))
    await user.click(screen.getByRole('button', { name: 'บันทึก' }))

    expect(await screen.findByText(/ไปเปลี่ยนตัวแปร EAC ที่หน้า EVM S-Curve ก่อน/)).toBeInTheDocument()
  })

  it('links to the EVM S-Curve screen where the variant is actually selected', async () => {
    loginAs('PM')
    vi.mocked(api.getProject).mockResolvedValueOnce(sampleProject)

    renderCard()
    await waitFor(() => expect(screen.getByLabelText(/Bottom-Up ETC/)).toBeInTheDocument())

    expect(screen.getByRole('link', { name: 'EVM S-Curve' })).toHaveAttribute(
      'href',
      '/app/project-1/evm',
    )
  })
})
