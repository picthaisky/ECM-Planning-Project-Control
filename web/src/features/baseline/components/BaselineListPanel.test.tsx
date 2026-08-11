import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { BaselineListPanel } from './BaselineListPanel'
import type { BaselineDto } from '../types'

const baselineA: BaselineDto = {
  id: 'baseline-a',
  projectId: 'project-1',
  name: 'Baseline A',
  isActive: true,
  capturedAt: '2026-07-01T00:00:00+07:00',
  capturedByUserId: 'user-1',
  bac: '1000000.00',
  activityCount: 250,
}
const baselineB: BaselineDto = { ...baselineA, id: 'baseline-b', name: 'Baseline B', isActive: false }

const noop = () => {}

describe('BaselineListPanel', () => {
  it('shows the honest "list not available" note when listAvailable is false, never silently claiming completeness', () => {
    render(
      <BaselineListPanel
        baselines={[baselineA]}
        loadState="ready"
        listAvailable={false}
        actionState="idle"
        actionError={null}
        canWrite
        onOpenCapture={noop}
        onActivate={noop}
        selectedBaselineId={null}
        onSelect={noop}
      />,
    )
    expect(screen.getByText(/ยังไม่มี endpoint สำหรับดึงรายการ Baseline ทั้งหมด/)).toBeInTheDocument()
  })

  it('omits the note when the real list loaded successfully', () => {
    render(
      <BaselineListPanel
        baselines={[baselineA]}
        loadState="ready"
        listAvailable
        actionState="idle"
        actionError={null}
        canWrite
        onOpenCapture={noop}
        onActivate={noop}
        selectedBaselineId={null}
        onSelect={noop}
      />,
    )
    expect(screen.queryByText(/ยังไม่มี endpoint/)).not.toBeInTheDocument()
  })

  it('marks the active baseline with a status pill, and only offers "ตั้งเป็น Active" on the inactive one', () => {
    render(
      <BaselineListPanel
        baselines={[baselineA, baselineB]}
        loadState="ready"
        listAvailable
        actionState="idle"
        actionError={null}
        canWrite
        onOpenCapture={noop}
        onActivate={noop}
        selectedBaselineId={null}
        onSelect={noop}
      />,
    )
    expect(screen.getByText('Active')).toBeInTheDocument()
    expect(screen.getAllByRole('button', { name: 'ตั้งเป็น Active' })).toHaveLength(1)
  })

  it('a role without write access sees neither the capture button nor "ตั้งเป็น Active"', () => {
    render(
      <BaselineListPanel
        baselines={[baselineA, baselineB]}
        loadState="ready"
        listAvailable
        actionState="idle"
        actionError={null}
        canWrite={false}
        onOpenCapture={noop}
        onActivate={noop}
        selectedBaselineId={null}
        onSelect={noop}
      />,
    )
    expect(screen.queryByRole('button', { name: '+ บันทึก Baseline ใหม่' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'ตั้งเป็น Active' })).not.toBeInTheDocument()
  })

  it('clicking a row selects it (toggle), clicking it again deselects', async () => {
    const onSelect = vi.fn()
    const user = userEvent.setup()
    render(
      <BaselineListPanel
        baselines={[baselineA]}
        loadState="ready"
        listAvailable
        actionState="idle"
        actionError={null}
        canWrite
        onOpenCapture={noop}
        onActivate={noop}
        selectedBaselineId={null}
        onSelect={onSelect}
      />,
    )

    await user.click(screen.getByText('Baseline A'))
    expect(onSelect).toHaveBeenCalledWith('baseline-a')
  })

  it('calls onActivate with the baseline id', async () => {
    const onActivate = vi.fn()
    const user = userEvent.setup()
    render(
      <BaselineListPanel
        baselines={[baselineB]}
        loadState="ready"
        listAvailable
        actionState="idle"
        actionError={null}
        canWrite
        onOpenCapture={noop}
        onActivate={onActivate}
        selectedBaselineId={null}
        onSelect={noop}
      />,
    )

    await user.click(screen.getByRole('button', { name: 'ตั้งเป็น Active' }))
    expect(onActivate).toHaveBeenCalledWith('baseline-b')
  })

  it('shows the empty-session message when there are no baselines yet', () => {
    render(
      <BaselineListPanel
        baselines={[]}
        loadState="ready"
        listAvailable={false}
        actionState="idle"
        actionError={null}
        canWrite
        onOpenCapture={noop}
        onActivate={noop}
        selectedBaselineId={null}
        onSelect={noop}
      />,
    )
    expect(screen.getByText('ยังไม่มี Baseline ที่สร้างในเซสชันนี้')).toBeInTheDocument()
  })
})
