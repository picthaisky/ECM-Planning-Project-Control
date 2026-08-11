import { afterAll, beforeAll, describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import { BaselineComparisonTable } from './BaselineComparisonTable'
import type { BaselineActivityDeltaDto } from '../types'

// jsdom performs no real layout, so the scroll container's offsetWidth/Height (what
// @tanstack/react-virtual reads to size the table's viewport) default to 0 — same stub as
// `components/DataTable.test.tsx`/`features/weather/components/WeatherLogTable.test.tsx`. The
// underlying virtualization mechanism itself (only the visible window + overscan is mounted for
// 10,000+ rows) is proven once, directly, in `components/DataTable.test.tsx` (ADR-0004 — "reuse
// that approach rather than inventing a second one") — this file only proves the column mapping.
beforeAll(() => {
  Object.defineProperty(HTMLElement.prototype, 'offsetHeight', { configurable: true, get: () => 480 })
  Object.defineProperty(HTMLElement.prototype, 'offsetWidth', { configurable: true, get: () => 1000 })
})

afterAll(() => {
  Reflect.deleteProperty(HTMLElement.prototype, 'offsetHeight')
  Reflect.deleteProperty(HTMLElement.prototype, 'offsetWidth')
})

// evm-formulas.md isn't the source here — this mirrors the one prototype mock row the backend
// confirmed actually reconciles with its own annotation: ACT-214 (start:30, dur:26, blStart:29,
// blDur:24 -> "ช้า 3 วัน").
const act214: BaselineActivityDeltaDto = {
  activityId: 'act-214',
  activityCode: 'ACT-214',
  name: 'งานโครงสร้างชั้น 4',
  isRemoved: false,
  baselinePlannedStart: '2026-06-29T00:00:00+07:00',
  baselinePlannedFinish: '2026-07-23T00:00:00+07:00',
  baselineDurationDays: 24,
  baselineBudgetCost: '2000000.00',
  currentPlannedStart: '2026-06-30T00:00:00+07:00',
  currentPlannedFinish: '2026-07-26T00:00:00+07:00',
  currentDurationDays: 26,
  currentBudgetCost: '2100000.00',
  isCritical: true,
  startVarianceDays: 1,
  finishVarianceDays: 3,
  durationVarianceDays: 2,
  budgetVarianceAmount: '100000.00',
}

const removedActivity: BaselineActivityDeltaDto = {
  activityId: 'act-999',
  activityCode: 'ACT-999',
  name: 'งานที่ถูกยกเลิก',
  isRemoved: true,
  baselinePlannedStart: '2026-06-01T00:00:00+07:00',
  baselinePlannedFinish: '2026-06-10T00:00:00+07:00',
  baselineDurationDays: 9,
  baselineBudgetCost: '50000.00',
  currentPlannedStart: null,
  currentPlannedFinish: null,
  currentDurationDays: null,
  currentBudgetCost: null,
  isCritical: null,
  startVarianceDays: null,
  finishVarianceDays: null,
  durationVarianceDays: null,
  budgetVarianceAmount: null,
}

describe('BaselineComparisonTable', () => {
  it('renders the activity code/name, variance days, and a Critical badge for the current schedule state', () => {
    render(<BaselineComparisonTable activities={[act214]} state="ready" />)

    expect(screen.getByText('ACT-214')).toBeInTheDocument()
    expect(screen.getByText('งานโครงสร้างชั้น 4')).toBeInTheDocument()
    expect(screen.getByText('+3 วัน (ช้า)')).toBeInTheDocument()
    expect(screen.getByText('+100,000.00 บาท')).toBeInTheDocument()
    // Two matches expected: the "Critical" column header and this row's own status pill.
    expect(screen.getAllByText('Critical')).toHaveLength(2)
  })

  it('a removed activity shows a "no longer in the current schedule" badge and dashes for every current-side field, never a fabricated 0', () => {
    render(<BaselineComparisonTable activities={[removedActivity]} state="ready" />)

    expect(screen.getByText('ไม่มีในกำหนดการปัจจุบันแล้ว')).toBeInTheDocument()
    expect(screen.getByText('ถูกลบออกจากกำหนดการ')).toBeInTheDocument()
    expect(screen.getAllByText('—').length).toBeGreaterThan(0)
  })

  it('never renders a "Critical Path เปลี่ยนเส้นทาง" claim — only the honest per-row current-criticality badge', () => {
    render(<BaselineComparisonTable activities={[act214]} state="ready" />)
    expect(screen.queryByText(/เปลี่ยนเส้นทาง/)).not.toBeInTheDocument()
  })

  it('shows the loading/error/empty states DataTable already owns', () => {
    const { rerender } = render(<BaselineComparisonTable activities={[]} state="loading" />)
    expect(screen.getByRole('status')).toBeInTheDocument()

    rerender(<BaselineComparisonTable activities={[]} state="error" errorMessage="โหลดไม่สำเร็จ" />)
    expect(screen.getByRole('alert')).toHaveTextContent('โหลดไม่สำเร็จ')

    rerender(<BaselineComparisonTable activities={[]} state="ready" />)
    expect(screen.getByText('ไม่มีข้อมูลกิจกรรมสำหรับเปรียบเทียบ')).toBeInTheDocument()
  })
})
