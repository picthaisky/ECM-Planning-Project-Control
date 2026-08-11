import { afterAll, beforeAll, describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ManpowerLogTable } from './ManpowerLogTable'
import type { ManpowerLogDto } from '../types'

// `DataTable` (ADR-0004) virtualizes rows via `@tanstack/react-virtual`, which sizes its visible
// window from the scroll container's real `offsetHeight` — jsdom reports 0 for both by default, so
// without this stub the virtualizer would compute zero visible rows and every row-content assertion
// below would fail for a reason unrelated to this component's own logic. Mirrors
// `features/weather/WeatherPage.test.tsx`'s identical, already-established stub.
beforeAll(() => {
  Object.defineProperty(HTMLElement.prototype, 'offsetHeight', { configurable: true, get: () => 440 })
  Object.defineProperty(HTMLElement.prototype, 'offsetWidth', { configurable: true, get: () => 900 })
})

afterAll(() => {
  Reflect.deleteProperty(HTMLElement.prototype, 'offsetHeight')
  Reflect.deleteProperty(HTMLElement.prototype, 'offsetWidth')
})

function makeRow(overrides: Partial<ManpowerLogDto> = {}): ManpowerLogDto {
  return {
    id: 'log-1',
    projectId: 'project-1',
    logDate: '2026-07-08T00:00:00.000Z',
    shift: 'Day',
    workCategoryId: '3fa85f64-5717-4562-b3fc-2c963f66afa6',
    wbsNodeId: null,
    activityId: null,
    labourType: 'OwnDirect',
    subcontractorRef: null,
    workerCount: 25,
    manHours: '200.00',
    overtimeHours: '0.00',
    manHoursDerived: false,
    equipmentCount: 2,
    equipmentOperatingHours: '12.00',
    equipmentStandbyHours: '4.00',
    workDescription: null,
    relatedWeatherLogId: null,
    recordedByUserId: 'user-1',
    recordedAt: '2026-07-08T09:00:00.000Z',
    entryKind: 'Original',
    correctsLogId: null,
    correctionReason: null,
    allowDuplicateOverride: false,
    ...overrides,
  }
}

describe('ManpowerLogTable', () => {
  it('shows an empty-state message explaining there is nothing recorded this session yet', () => {
    render(<ManpowerLogTable rows={[]} canWrite onRequestCorrection={vi.fn()} />)
    expect(screen.getByText(/ยังไม่มีบันทึกในเซสชันนี้/)).toBeInTheDocument()
  })

  it('renders a recorded row with worker count and man-hours', () => {
    render(<ManpowerLogTable rows={[makeRow()]} canWrite onRequestCorrection={vi.fn()} />)
    expect(screen.getByText('25')).toBeInTheDocument()
    expect(screen.getByText('200.00')).toBeInTheDocument()
  })

  it('offers a "แก้ไข" action for a writable, non-retracted row and calls back with the row', async () => {
    const onRequestCorrection = vi.fn()
    render(<ManpowerLogTable rows={[makeRow()]} canWrite onRequestCorrection={onRequestCorrection} />)

    await userEvent.click(screen.getByRole('button', { name: 'แก้ไข' }))
    expect(onRequestCorrection).toHaveBeenCalledWith(expect.objectContaining({ id: 'log-1' }))
  })

  it('hides the "แก้ไข" action for a read-only viewer', () => {
    render(<ManpowerLogTable rows={[makeRow()]} canWrite={false} onRequestCorrection={vi.fn()} />)
    expect(screen.queryByRole('button', { name: 'แก้ไข' })).not.toBeInTheDocument()
  })

  it('does not offer "แก้ไข" on an already-retracted row', () => {
    render(<ManpowerLogTable rows={[makeRow({ entryKind: 'Retraction' })]} canWrite onRequestCorrection={vi.fn()} />)
    expect(screen.queryByRole('button', { name: 'แก้ไข' })).not.toBeInTheDocument()
  })
})
