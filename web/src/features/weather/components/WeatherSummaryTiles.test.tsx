import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { WeatherSummaryTiles } from './WeatherSummaryTiles'
import type { WeatherLogDto } from '../types'

function makeLog(overrides: Partial<WeatherLogDto> & { id: string }): WeatherLogDto {
  return {
    projectId: 'project-1',
    logDate: '2026-07-08T00:00:00Z',
    condition: 'HeavyRain',
    conditionNote: null,
    rainfallMm: '61.00',
    impact: 'FullStoppage',
    impactNote: null,
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

describe('WeatherSummaryTiles', () => {
  it('renders the three real, data-backed tiles (no fabricated forecast tile)', () => {
    render(<WeatherSummaryTiles logs={[makeLog({ id: 'e1' })]} state="ready" />)

    expect(screen.getByText('วันที่มีฝนตกบันทึกไว้')).toBeInTheDocument()
    expect(screen.getByText('วันหยุดงานจากสภาพอากาศ')).toBeInTheDocument()
    expect(screen.getByText('บันทึกที่ยังไม่ระบุกิจกรรม')).toBeInTheDocument()
    expect(screen.queryByText('พยากรณ์พรุ่งนี้')).not.toBeInTheDocument()
    expect(screen.queryByText(/สิทธิ์ขยายสัญญา/)).not.toBeInTheDocument()
  })

  it('shows a loading state while logs are loading', () => {
    render(<WeatherSummaryTiles logs={[]} state="loading" />)
    expect(screen.getAllByRole('status')).toHaveLength(3)
  })

  it('offers a discoverable "correct it" prompt when there are unattributed entries', async () => {
    const onFocusUnattributed = vi.fn()
    render(<WeatherSummaryTiles logs={[makeLog({ id: 'e1', affectedActivityIds: [] })]} state="ready" onFocusUnattributed={onFocusUnattributed} />)

    await userEvent.click(screen.getByRole('button', { name: /ยื่นบันทึกแก้ไขเพื่อระบุกิจกรรม/ }))
    expect(onFocusUnattributed).toHaveBeenCalledTimes(1)
  })

  it('shows no correction prompt when there is nothing unattributed', () => {
    render(<WeatherSummaryTiles logs={[makeLog({ id: 'e1' })]} state="ready" onFocusUnattributed={vi.fn()} />)
    expect(screen.queryByRole('button', { name: /ยื่นบันทึกแก้ไขเพื่อระบุกิจกรรม/ })).not.toBeInTheDocument()
  })
})
