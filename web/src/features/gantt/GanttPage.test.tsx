import { afterAll, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { GanttPage } from './GanttPage'
import * as api from './api'
import type { GanttDto } from './types'

vi.mock('./api', async () => {
  const actual = await vi.importActual<typeof import('./api')>('./api')
  return { ...actual, getGantt: vi.fn() }
})

beforeAll(() => {
  Object.defineProperty(HTMLElement.prototype, 'offsetHeight', { configurable: true, get: () => 520 })
  Object.defineProperty(HTMLElement.prototype, 'offsetWidth', { configurable: true, get: () => 900 })
  Object.defineProperty(HTMLElement.prototype, 'clientHeight', { configurable: true, get: () => 520 })
  Object.defineProperty(HTMLElement.prototype, 'clientWidth', { configurable: true, get: () => 900 })
})

afterAll(() => {
  Reflect.deleteProperty(HTMLElement.prototype, 'offsetHeight')
  Reflect.deleteProperty(HTMLElement.prototype, 'offsetWidth')
  Reflect.deleteProperty(HTMLElement.prototype, 'clientHeight')
  Reflect.deleteProperty(HTMLElement.prototype, 'clientWidth')
})

const sampleGantt: GanttDto = {
  projectId: 'project-1',
  dataDate: '2026-03-15T00:00:00+07:00',
  activities: [
    {
      id: 'a1',
      wbsNodeId: 'node-1',
      activityCode: 'ACT-101',
      name: 'งานเสาเข็มและฐานราก',
      plannedStart: '2026-01-01T00:00:00+07:00',
      plannedFinish: '2026-01-16T00:00:00+07:00',
      actualStart: null,
      actualFinish: null,
      isCritical: false,
      totalFloat: 5,
      freeFloat: 5,
    },
  ],
}

function renderPage() {
  return render(
    <MemoryRouter initialEntries={['/app/project-1/gantt']}>
      <Routes>
        <Route path="/app/:projectId/gantt" element={<GanttPage />} />
      </Routes>
    </MemoryRouter>,
  )
}

describe('GanttPage', () => {
  beforeEach(() => {
    vi.mocked(api.getGantt).mockReset()
  })

  it('shows a loading state, then the real Gantt chart once data resolves', async () => {
    vi.mocked(api.getGantt).mockResolvedValueOnce(sampleGantt)
    renderPage()

    expect(screen.getByText('กำลังโหลดข้อมูล Gantt...')).toBeInTheDocument()

    await waitFor(() => expect(screen.getByText('งานเสาเข็มและฐานราก')).toBeInTheDocument())
  })

  it('shows a Thai error state on load failure, never a blank/broken screen', async () => {
    vi.mocked(api.getGantt).mockRejectedValueOnce(new api.GanttApiError('ไม่พบโครงการที่ระบุ', 404))
    renderPage()

    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent('ไม่พบโครงการที่ระบุ'))
  })

  it('passes the real GanttDto.dataDate through to GanttChart\'s data-date line (gap closed post-S6-FE-01)', async () => {
    vi.mocked(api.getGantt).mockResolvedValueOnce(sampleGantt)
    renderPage()

    await waitFor(() => expect(screen.getByText('งานเสาเข็มและฐานราก')).toBeInTheDocument())
    // The real caption prefix (GanttChart.tsx's dataDateCaptionFormatter output) - not the
    // "no data date" fallback message, which this test used to (correctly, at the time) assert.
    expect(screen.getByText(/เส้นประสีกรมท่าแนวตั้ง = Data date/)).toBeInTheDocument()
    expect(screen.queryByText(/ยังไม่มีข้อมูล Data Date จาก Project/)).not.toBeInTheDocument()
  })

  it('still states the gap honestly if a project genuinely has no data date (defensive, not expected in practice)', async () => {
    vi.mocked(api.getGantt).mockResolvedValueOnce({ ...sampleGantt, dataDate: '' })
    renderPage()

    await waitFor(() => expect(screen.getByText('งานเสาเข็มและฐานราก')).toBeInTheDocument())
    expect(screen.getByText(/ยังไม่มีข้อมูล Data Date จาก Project/)).toBeInTheDocument()
  })
})
