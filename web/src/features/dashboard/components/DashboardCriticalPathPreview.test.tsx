import type { ComponentProps } from 'react'
import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it } from 'vitest'
import { DashboardCriticalPathPreview } from './DashboardCriticalPathPreview'
import type { GanttActivityDto } from '../../gantt'

function activity(overrides: Partial<GanttActivityDto>): GanttActivityDto {
  return {
    id: 'a1',
    wbsNodeId: 'w1',
    activityCode: 'A-100',
    name: 'เทคอนกรีตฐานราก',
    plannedStart: '2026-01-01T00:00:00+07:00',
    plannedFinish: '2026-08-20T00:00:00+07:00',
    actualStart: null,
    actualFinish: null,
    isCritical: true,
    totalFloat: 0,
    freeFloat: 0,
    ...overrides,
  }
}

function renderPreview(props: Partial<ComponentProps<typeof DashboardCriticalPathPreview>> = {}) {
  return render(
    <MemoryRouter>
      <DashboardCriticalPathPreview
        projectId="project-1"
        activities={[]}
        loadState="ready"
        loadError={null}
        {...props}
      />
    </MemoryRouter>,
  )
}

describe('DashboardCriticalPathPreview (S8-FE-01)', () => {
  it('renders the top 4 critical activities soonest-due-first, from real Gantt/CPM data (never fabricated)', () => {
    const activities: GanttActivityDto[] = [
      activity({ id: 'late', name: 'งานหลังคา', plannedFinish: '2026-12-01T00:00:00+07:00' }),
      activity({ id: 'not-critical', name: 'งานไม่วิกฤต', isCritical: false, plannedFinish: '2026-01-05T00:00:00+07:00' }),
      activity({ id: 'soonest', name: 'งานฐานราก', plannedFinish: '2026-08-01T00:00:00+07:00' }),
    ]

    renderPreview({ activities })

    const items = screen.getAllByRole('listitem')
    expect(items).toHaveLength(2) // only the 2 critical activities, "not-critical" excluded
    expect(items[0]).toHaveTextContent('งานฐานราก')
    expect(items[1]).toHaveTextContent('งานหลังคา')
  })

  it('shows the activity code, formatted finish date, and total float badge', () => {
    renderPreview({
      activities: [activity({ activityCode: 'A-201', totalFloat: 0, plannedFinish: '2026-08-20T00:00:00+07:00' })],
    })

    expect(screen.getByText(/A-201/)).toBeInTheDocument()
    expect(screen.getByText(/20 ส.ค. 2569/)).toBeInTheDocument()
    expect(screen.getByText('TF=0')).toBeInTheDocument()
  })

  it('honest empty state when there are no critical activities (e.g. CPM never run) — never a blank card', () => {
    renderPreview({ activities: [] })
    expect(screen.getByText(/ยังไม่พบกิจกรรมวิกฤต/)).toBeInTheDocument()
  })

  it('shows a loading state', () => {
    renderPreview({ loadState: 'loading' })
    expect(screen.getByRole('status')).toHaveTextContent('กำลังโหลดข้อมูล Critical Path')
  })

  it('shows a Thai error state, never a blank/broken widget', () => {
    renderPreview({ loadState: 'error', loadError: 'โหลดข้อมูล Gantt ไม่สำเร็จ' })
    expect(screen.getByRole('alert')).toHaveTextContent('โหลดข้อมูล Gantt ไม่สำเร็จ')
  })

  it('links through to the real Gantt screen', () => {
    renderPreview()
    expect(screen.getByRole('link', { name: /ดู Gantt Chart ทั้งหมด/ })).toHaveAttribute(
      'href',
      '/app/project-1/gantt',
    )
  })
})
