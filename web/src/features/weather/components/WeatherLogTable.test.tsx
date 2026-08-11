import { afterAll, beforeAll, describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { WeatherLogTable } from './WeatherLogTable'
import type { WeatherLogDto } from '../types'

// jsdom performs no real layout, so the scroll container's offsetWidth/Height (what
// @tanstack/react-virtual reads to size the table's viewport) default to 0 — same stub as
// `features/vo/VoPage.test.tsx`/`components/DataTable.test.tsx`.
beforeAll(() => {
  Object.defineProperty(HTMLElement.prototype, 'offsetHeight', { configurable: true, get: () => 440 })
  Object.defineProperty(HTMLElement.prototype, 'offsetWidth', { configurable: true, get: () => 900 })
})

afterAll(() => {
  Reflect.deleteProperty(HTMLElement.prototype, 'offsetHeight')
  Reflect.deleteProperty(HTMLElement.prototype, 'offsetWidth')
})

function makeLog(overrides: Partial<WeatherLogDto> & { id: string }): WeatherLogDto {
  return {
    projectId: 'project-1',
    logDate: '2026-07-08T00:00:00Z',
    condition: 'HeavyRain',
    conditionNote: null,
    rainfallMm: '61.00',
    impact: 'FullStoppage',
    impactNote: 'หยุดงานภายนอกทั้งวัน',
    hoursLost: '8.00',
    workStoppage: true,
    entryKind: 'Original',
    correctsWeatherLogId: null,
    correctionReason: null,
    affectedActivityIds: ['activity-1'],
    recordedByUserId: 'user-1',
    recordedAt: '2026-07-08T09:00:00Z',
    ...overrides,
  }
}

describe('WeatherLogTable', () => {
  it('shows the correction action only on the current chain tail, never on a superseded row', () => {
    const original = makeLog({ id: 'e1' })
    const correction = makeLog({ id: 'e2', entryKind: 'Correction', correctsWeatherLogId: 'e1', correctionReason: 'r' })

    render(<WeatherLogTable logs={[correction, original]} state="ready" canWrite onRequestCorrection={vi.fn()} />)

    expect(screen.getAllByRole('button', { name: 'แก้ไข/ยกเลิกรายการ' })).toHaveLength(1)
  })

  it('never offers the correction action on a Retraction row (deliberate scope decision)', () => {
    const retraction = makeLog({ id: 'e1', entryKind: 'Retraction', correctionReason: 'r' })
    render(<WeatherLogTable logs={[retraction]} state="ready" canWrite onRequestCorrection={vi.fn()} />)
    expect(screen.queryByRole('button', { name: 'แก้ไข/ยกเลิกรายการ' })).not.toBeInTheDocument()
  })

  it('hides the correction action entirely when canWrite is false, regardless of chain state', () => {
    render(<WeatherLogTable logs={[makeLog({ id: 'e1' })]} state="ready" canWrite={false} onRequestCorrection={vi.fn()} />)
    expect(screen.queryByRole('button', { name: 'แก้ไข/ยกเลิกรายการ' })).not.toBeInTheDocument()
  })

  it('calls onRequestCorrection with the row entry when the action is clicked', async () => {
    const log = makeLog({ id: 'e1' })
    const onRequestCorrection = vi.fn()
    render(<WeatherLogTable logs={[log]} state="ready" canWrite onRequestCorrection={onRequestCorrection} />)

    await userEvent.click(screen.getByRole('button', { name: 'แก้ไข/ยกเลิกรายการ' }))
    expect(onRequestCorrection).toHaveBeenCalledWith(log)
  })

  it('labels the effective/corrected/retracted chain states distinctly', () => {
    const original = makeLog({ id: 'e1' })
    const correction = makeLog({ id: 'e2', entryKind: 'Correction', correctsWeatherLogId: 'e1', correctionReason: 'r' })
    render(<WeatherLogTable logs={[correction, original]} state="ready" canWrite={false} onRequestCorrection={vi.fn()} />)

    expect(screen.getByText('ถูกแก้ไขแล้ว')).toBeInTheDocument()
    expect(screen.getByText('ปัจจุบัน (มีผล)')).toBeInTheDocument()
  })

  it('prompts to correct an in-force, unattributed stoppage with the domain-specified copy', () => {
    const log = makeLog({ id: 'e1', affectedActivityIds: [] })
    render(<WeatherLogTable logs={[log]} state="ready" canWrite={false} onRequestCorrection={vi.fn()} />)
    expect(screen.getByText('ยังไม่ได้ระบุกิจกรรมที่ได้รับผลกระทบ — ยื่นบันทึกแก้ไขเพื่อระบุ')).toBeInTheDocument()
  })

  it('onlyUnattributed filters the rows down to in-force, unattributed stoppages', () => {
    const attributed = makeLog({ id: 'e1' })
    const unattributed = makeLog({ id: 'e2', affectedActivityIds: [] })
    render(<WeatherLogTable logs={[attributed, unattributed]} state="ready" canWrite={false} onlyUnattributed onRequestCorrection={vi.fn()} />)

    expect(screen.getByRole('table')).toHaveAttribute('aria-rowcount', '1')
  })

  it('shows the empty state when there are no rows', () => {
    render(<WeatherLogTable logs={[]} state="ready" canWrite={false} onRequestCorrection={vi.fn()} />)
    expect(screen.getByText('ยังไม่มีบันทึกสภาพอากาศในโครงการนี้')).toBeInTheDocument()
  })
})
