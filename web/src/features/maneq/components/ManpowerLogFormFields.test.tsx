import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ManpowerLogFormFields } from './ManpowerLogFormFields'
import { emptyManpowerLogFormValues } from '../maneqForm'
import type { WbsNodeOptionDto, WorkCategoryDto } from '../types'

const categories: WorkCategoryDto[] = [
  { id: 'cat-gen', code: 'GEN', nameTh: 'งานทั่วไป', nameEn: 'General', displayOrder: 1 },
  { id: 'cat-str', code: 'STR', nameTh: 'งานโครงสร้าง', nameEn: 'Structural', displayOrder: 2 },
]

const wbsNodes: WbsNodeOptionDto[] = [
  { id: 'node-1', code: '1.1', title: 'ฐานราก' },
  { id: 'node-2', code: '1.2', title: 'เสาเข็ม' },
]

describe('ManpowerLogFormFields — work-category field', () => {
  it('renders a dropdown when the catalogue is available; selecting an option sets workCategoryId to its id', async () => {
    const onChange = vi.fn()
    render(<ManpowerLogFormFields values={emptyManpowerLogFormValues()} onChange={onChange} workCategories={categories} />)

    const field = screen.getByLabelText(/Work Category/)
    expect(field.tagName).toBe('SELECT')
    expect(screen.getByRole('option', { name: /งานโครงสร้าง \(STR\)/ })).toBeInTheDocument()

    await userEvent.selectOptions(field, 'cat-str')
    expect(onChange).toHaveBeenCalledWith({ workCategoryId: 'cat-str' })
  })

  it('falls back to a raw-GUID text input when the catalogue is empty/unavailable, so logging is never blocked', () => {
    render(<ManpowerLogFormFields values={emptyManpowerLogFormValues()} onChange={vi.fn()} workCategories={[]} />)

    const field = screen.getByLabelText(/Work Category/)
    expect(field.tagName).toBe('INPUT')
  })

  it('renders a WBS-node dropdown when the tree is available; selecting one sets wbsNodeId', async () => {
    const onChange = vi.fn()
    render(<ManpowerLogFormFields values={emptyManpowerLogFormValues()} onChange={onChange} wbsNodes={wbsNodes} />)

    const field = screen.getByLabelText(/WBS Node/)
    expect(field.tagName).toBe('SELECT')
    await userEvent.selectOptions(field, 'node-2')
    expect(onChange).toHaveBeenCalledWith({ wbsNodeId: 'node-2' })
  })

  it('renders a dependent activity dropdown when a node has activities; selecting one sets activityId', async () => {
    const onChange = vi.fn()
    const activities = [
      { id: 'act-1', activityCode: 'A-01', name: 'ตอกเสาเข็ม' },
      { id: 'act-2', activityCode: 'A-02', name: 'เทฐานราก' },
    ]
    render(<ManpowerLogFormFields values={emptyManpowerLogFormValues()} onChange={onChange} activities={activities} />)

    const field = screen.getByLabelText(/Activity/)
    expect(field.tagName).toBe('SELECT')
    await userEvent.selectOptions(field, 'act-2')
    expect(onChange).toHaveBeenCalledWith({ activityId: 'act-2' })
  })

  it('renders a related-weather-log dropdown when logs are available; selecting one sets relatedWeatherLogId', async () => {
    const onChange = vi.fn()
    const weatherLogs = [
      { id: 'wx-1', logDate: '2026-06-01T00:00:00+07:00', condition: 'HeavyRain' },
      { id: 'wx-2', logDate: '2026-06-02T00:00:00+07:00', condition: 'Storm' },
    ]
    render(<ManpowerLogFormFields values={emptyManpowerLogFormValues()} onChange={onChange} weatherLogs={weatherLogs} />)

    const field = screen.getByLabelText(/Related Weather Log/)
    expect(field.tagName).toBe('SELECT')
    expect(screen.getByRole('option', { name: /2026-06-01 — HeavyRain/ })).toBeInTheDocument()
    await userEvent.selectOptions(field, 'wx-2')
    expect(onChange).toHaveBeenCalledWith({ relatedWeatherLogId: 'wx-2' })
  })
})
