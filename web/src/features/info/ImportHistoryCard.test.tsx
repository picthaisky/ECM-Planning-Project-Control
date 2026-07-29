import { afterAll, beforeAll, describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ImportHistoryCard } from './ImportHistoryCard'
import type { FileImportJob } from './types'

// jsdom performs no real layout, so DataTable's virtualizer would otherwise compute a zero-size
// viewport and render no rows at all — see DataTable.test.tsx for the same stub.
beforeAll(() => {
  Object.defineProperty(HTMLElement.prototype, 'offsetHeight', {
    configurable: true,
    get() {
      return 280
    },
  })
  Object.defineProperty(HTMLElement.prototype, 'offsetWidth', {
    configurable: true,
    get() {
      return 800
    },
  })
})

afterAll(() => {
  Reflect.deleteProperty(HTMLElement.prototype, 'offsetHeight')
  Reflect.deleteProperty(HTMLElement.prototype, 'offsetWidth')
})

const succeededJob: FileImportJob = {
  id: 'job-1',
  projectId: 'p1',
  fileName: 'baseline_rev2.xer',
  format: 'Xer',
  status: 'Succeeded',
  rowsImported: 128,
  errorJson: null,
  startedAt: '2026-07-08T09:00:00+07:00',
  finishedAt: '2026-07-08T09:00:05+07:00',
  createdByUserId: 'u1',
}

const failedJob: FileImportJob = {
  id: 'job-2',
  projectId: 'p1',
  fileName: 'schedule_v3.xml',
  format: 'Mspdi',
  status: 'Failed',
  rowsImported: 0,
  errorJson: JSON.stringify({
    code: 'ImportMalformedFile',
    detail: 'Task UID 55 has an unparsable Start/Finish.',
  }),
  startedAt: '2026-07-01T09:00:00+07:00',
  finishedAt: '2026-07-01T09:00:02+07:00',
  createdByUserId: 'u1',
}

describe('ImportHistoryCard', () => {
  it('loading state: shows the DataTable loading indicator', () => {
    render(<ImportHistoryCard jobs={[]} state="loading" errorMessage={null} onRetry={vi.fn()} />)
    expect(screen.getByRole('status')).toBeInTheDocument()
  })

  it('error state: shows the provided Thai error message', () => {
    render(
      <ImportHistoryCard
        jobs={[]}
        state="error"
        errorMessage="โหลดประวัติการนำเข้าไม่สำเร็จ"
        onRetry={vi.fn()}
      />,
    )
    expect(screen.getByRole('alert')).toHaveTextContent('โหลดประวัติการนำเข้าไม่สำเร็จ')
  })

  it('empty state: shows the empty message when there is no history yet', () => {
    render(<ImportHistoryCard jobs={[]} state="ready" errorMessage={null} onRetry={vi.fn()} />)
    expect(screen.getByText('ยังไม่มีประวัติการนำเข้าไฟล์สำหรับโครงการนี้')).toBeInTheDocument()
  })

  it('happy path: renders file name, format, row count, and status for each job', () => {
    render(
      <ImportHistoryCard
        jobs={[succeededJob, failedJob]}
        state="ready"
        errorMessage={null}
        onRetry={vi.fn()}
      />,
    )

    expect(screen.getByText('baseline_rev2.xer')).toBeInTheDocument()
    expect(screen.getByText('128')).toBeInTheDocument()
    expect(screen.getByText('schedule_v3.xml')).toBeInTheDocument()
    expect(screen.getByText('Succeeded')).toBeInTheDocument()
    expect(screen.getByText('Failed')).toBeInTheDocument()
  })

  it('opens a detail modal with the Thai-translated error for a failed job, and closes it', async () => {
    const user = userEvent.setup()
    render(
      <ImportHistoryCard jobs={[failedJob]} state="ready" errorMessage={null} onRetry={vi.fn()} />,
    )

    await user.click(screen.getByRole('button', { name: 'ดูสาเหตุ' }))

    const dialog = screen.getByRole('dialog')
    expect(dialog).toHaveTextContent('รูปแบบไฟล์ไม่ถูกต้อง')
    expect(dialog).toHaveTextContent('กิจกรรม Task UID 55')

    await user.click(screen.getByRole('button', { name: 'ปิด' }))
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('a succeeded job has no detail action', () => {
    render(
      <ImportHistoryCard
        jobs={[succeededJob]}
        state="ready"
        errorMessage={null}
        onRetry={vi.fn()}
      />,
    )
    expect(screen.queryByRole('button', { name: 'ดูสาเหตุ' })).not.toBeInTheDocument()
  })

  it('refresh button calls onRetry and is disabled while loading', async () => {
    const onRetry = vi.fn()
    const user = userEvent.setup()
    const { rerender } = render(
      <ImportHistoryCard
        jobs={[succeededJob]}
        state="ready"
        errorMessage={null}
        onRetry={onRetry}
      />,
    )

    await user.click(screen.getByRole('button', { name: 'รีเฟรช' }))
    expect(onRetry).toHaveBeenCalledTimes(1)

    rerender(
      <ImportHistoryCard
        jobs={[succeededJob]}
        state="loading"
        errorMessage={null}
        onRetry={onRetry}
      />,
    )
    expect(screen.getByRole('button', { name: 'รีเฟรช' })).toBeDisabled()
  })
})
