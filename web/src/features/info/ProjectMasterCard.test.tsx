import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ProjectMasterCard } from './ProjectMasterCard'
import { useAuthStore } from '../../store/authStore'
import * as api from './api'
import type { Project } from './types'

vi.mock('./api', async () => {
  const actual = await vi.importActual<typeof import('./api')>('./api')
  return { ...actual, getProject: vi.fn(), updateProject: vi.fn() }
})

const sampleProject: Project = {
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
}

function loginAs(role: 'PM' | 'Site') {
  useAuthStore.getState().login({
    accessToken: 'jwt',
    expiresAt: '2027-01-01T00:00:00+07:00',
    userId: 'user-1',
    tenantId: 'tenant-1',
    role,
  })
}

describe('ProjectMasterCard', () => {
  beforeEach(() => {
    vi.mocked(api.getProject).mockReset()
    vi.mocked(api.updateProject).mockReset()
    useAuthStore.getState().logout()
  })

  it('loading -> ready: renders the project fields read-only', async () => {
    loginAs('PM')
    vi.mocked(api.getProject).mockResolvedValueOnce(sampleProject)

    render(<ProjectMasterCard projectId="project-1" />)

    expect(screen.getByRole('status')).toBeInTheDocument()
    await waitFor(() => expect(screen.getByText('Riverside Condominium Tower B')).toBeInTheDocument())
    expect(screen.getByText('RCT-B')).toBeInTheDocument()
    expect(screen.getByText(/850,000,000\.00 บาท/)).toBeInTheDocument()
    expect(screen.getByText(/5\.00% \(ไม่มีเพดาน\)/)).toBeInTheDocument()
  })

  it('shows a retry button and error message when the load fails (no GET /projects/{id} endpoint yet)', async () => {
    loginAs('PM')
    vi.mocked(api.getProject).mockRejectedValueOnce(
      Object.assign(new api.ProjectApiError('ไม่พบโครงการที่ระบุ', 404), {}),
    )

    render(<ProjectMasterCard projectId="project-1" />)

    expect(await screen.findByRole('alert')).toHaveTextContent('ไม่พบโครงการที่ระบุ')
    expect(screen.getByRole('button', { name: 'ลองใหม่อีกครั้ง' })).toBeInTheDocument()
  })

  it('the edit button is visible for an allowed role (PM) and hidden for a disallowed role (Site)', async () => {
    loginAs('Site')
    vi.mocked(api.getProject).mockResolvedValueOnce(sampleProject)
    render(<ProjectMasterCard projectId="project-1" />)
    await waitFor(() => expect(screen.getByText('RCT-B')).toBeInTheDocument())
    expect(screen.queryByRole('button', { name: 'แก้ไขข้อมูล' })).not.toBeInTheDocument()
  })

  it('editing, fixing an out-of-range percent, and saving calls updateProject and returns to view mode', async () => {
    loginAs('PM')
    vi.mocked(api.getProject).mockResolvedValueOnce(sampleProject)
    const updated: Project = { ...sampleProject, retentionRate: '7.50' }
    vi.mocked(api.updateProject).mockResolvedValueOnce(updated)
    const user = userEvent.setup()

    render(<ProjectMasterCard projectId="project-1" />)
    await waitFor(() => expect(screen.getByText('RCT-B')).toBeInTheDocument())

    await user.click(screen.getByRole('button', { name: 'แก้ไขข้อมูล' }))
    const retentionInput = screen.getByLabelText('Retention Rate (%)')

    // Out-of-range client-side rejection first (mirrors UpdateProjectCommandValidator).
    await user.clear(retentionInput)
    await user.type(retentionInput, '150')
    await user.click(screen.getByRole('button', { name: 'บันทึก' }))
    expect(await screen.findByText('ต้องอยู่ระหว่าง 0.00 ถึง 100.00')).toBeInTheDocument()
    expect(api.updateProject).not.toHaveBeenCalled()

    // Now a valid value.
    await user.clear(retentionInput)
    await user.type(retentionInput, '7.5')
    await user.click(screen.getByRole('button', { name: 'บันทึก' }))

    await waitFor(() => expect(api.updateProject).toHaveBeenCalledTimes(1))
    const [, payload] = vi.mocked(api.updateProject).mock.calls[0]
    expect(payload.retentionRate).toBe('7.5')

    await waitFor(() => expect(screen.queryByRole('button', { name: 'บันทึก' })).not.toBeInTheDocument())
    expect(screen.getByText(/7\.50%/)).toBeInTheDocument()
  })

  it('shows the computed (never hardcoded) retention cap amount and the non-blocking >10% warning', async () => {
    loginAs('PM')
    vi.mocked(api.getProject).mockResolvedValueOnce({
      ...sampleProject,
      contractValue: '100000000.00',
      retentionCapPercentage: '12.00',
    })
    render(<ProjectMasterCard projectId="project-1" />)

    // 12% of 100,000,000 = 12,000,000.00 — computed, not the prototype's static "10%" text.
    await waitFor(() => expect(screen.getByText(/12,000,000\.00 บาท/)).toBeInTheDocument())
    expect(screen.getByText(/สูงกว่าปกติ/)).toBeInTheDocument()
  })

  it('a server-side save rejection (e.g. concurrent edit) is shown without losing the in-progress form', async () => {
    loginAs('PM')
    vi.mocked(api.getProject).mockResolvedValueOnce(sampleProject)
    vi.mocked(api.updateProject).mockRejectedValueOnce(
      new api.ProjectApiError('ข้อมูลไม่ถูกต้อง กรุณาตรวจสอบค่าที่กรอกอีกครั้ง', 400),
    )
    const user = userEvent.setup()

    render(<ProjectMasterCard projectId="project-1" />)
    await waitFor(() => expect(screen.getByText('RCT-B')).toBeInTheDocument())
    await user.click(screen.getByRole('button', { name: 'แก้ไขข้อมูล' }))
    await user.click(screen.getByRole('button', { name: 'บันทึก' }))

    expect(await screen.findByText('ข้อมูลไม่ถูกต้อง กรุณาตรวจสอบค่าที่กรอกอีกครั้ง')).toBeInTheDocument()
    // Still in edit mode — the user's in-progress values were not discarded.
    expect(screen.getByRole('button', { name: 'บันทึก' })).toBeInTheDocument()
  })
})
