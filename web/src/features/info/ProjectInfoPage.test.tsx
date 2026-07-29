import { render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ProjectInfoPage } from './ProjectInfoPage'
import * as api from './api'
import type { Project } from './types'

vi.mock('./api', async () => {
  const actual = await vi.importActual<typeof import('./api')>('./api')
  return {
    ...actual,
    getProject: vi.fn(),
    getImportJobHistory: vi.fn(),
  }
})

const sampleProject: Project = {
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

describe('ProjectInfoPage', () => {
  beforeEach(() => {
    vi.mocked(api.getProject).mockReset()
    vi.mocked(api.getImportJobHistory).mockReset()
  })

  it('renders the project master card and the import wizard side by side, both bound to the routed projectId', async () => {
    vi.mocked(api.getProject).mockResolvedValueOnce(sampleProject)
    vi.mocked(api.getImportJobHistory).mockResolvedValueOnce([])

    render(
      <MemoryRouter initialEntries={['/app/project-1/info']}>
        <Routes>
          <Route path="/app/:projectId/info" element={<ProjectInfoPage />} />
        </Routes>
      </MemoryRouter>,
    )

    await waitFor(() => expect(screen.getByText('RCT-B')).toBeInTheDocument())
    expect(api.getProject).toHaveBeenCalledWith('project-1')
    expect(screen.getByText('นำเข้าข้อมูลแผนงาน (Import)')).toBeInTheDocument()
    expect(api.getImportJobHistory).toHaveBeenCalledWith('project-1')
  })
})
