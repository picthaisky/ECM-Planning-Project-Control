import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { ProjectSettingsModal } from './ProjectSettingsModal'
import type { Project } from '../../info'

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

describe('ProjectSettingsModal', () => {
  it('empty state: renders nothing when closed', () => {
    render(
      <ProjectSettingsModal isOpen={false} onClose={vi.fn()} project={project} saving={false} serverError={null} onSubmit={vi.fn()} />,
    )
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('renders the real ProjectEditForm (the single source of truth) when a project is available', () => {
    render(
      <ProjectSettingsModal isOpen onClose={vi.fn()} project={project} saving={false} serverError={null} onSubmit={vi.fn()} />,
    )
    expect(screen.getByRole('form', { name: 'แก้ไขข้อมูลโครงการ' })).toBeInTheDocument()
    expect(screen.getByDisplayValue('5.00')).toBeInTheDocument() // retentionRate field, pre-filled
  })

  it('loading state: shows a loading note instead of the form while the project has not loaded yet', () => {
    render(<ProjectSettingsModal isOpen onClose={vi.fn()} project={null} saving={false} serverError={null} onSubmit={vi.fn()} />)
    expect(screen.getByText('กำลังโหลดข้อมูลโครงการ...')).toBeInTheDocument()
    expect(screen.queryByRole('form')).not.toBeInTheDocument()
  })
})
