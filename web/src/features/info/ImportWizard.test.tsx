import { afterAll, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { ImportWizard } from './ImportWizard'
import * as api from './api'
import type { FileImportJob } from './types'

vi.mock('./api', async () => {
  const actual = await vi.importActual<typeof import('./api')>('./api')
  return {
    ...actual,
    importFile: vi.fn(),
    getImportJob: vi.fn(),
    getImportJobHistory: vi.fn(),
    downloadProgressTemplate: vi.fn(),
  }
})

// DataTable's virtualizer needs a non-zero viewport under jsdom (see DataTable.test.tsx).
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
  projectId: 'project-1',
  fileName: 'schedule.xer',
  format: 'Xer',
  status: 'Succeeded',
  rowsImported: 7,
  errorJson: null,
  startedAt: '2026-07-27T09:00:00+07:00',
  finishedAt: '2026-07-27T09:00:02+07:00',
  createdByUserId: 'user-1',
}

describe('ImportWizard (end-to-end wiring: upload -> progress -> result -> history)', () => {
  beforeEach(() => {
    vi.mocked(api.importFile).mockReset()
    vi.mocked(api.getImportJobHistory).mockReset()
  })

  it('uploading a file shows the Thai success result and the new job appears in history', async () => {
    vi.mocked(api.getImportJobHistory).mockResolvedValueOnce([]) // initial mount: no history yet
    vi.mocked(api.importFile).mockResolvedValueOnce(succeededJob)
    vi.mocked(api.getImportJobHistory).mockResolvedValueOnce([succeededJob]) // post-import refresh

    render(<ImportWizard projectId="project-1" />)

    await waitFor(() =>
      expect(screen.getByText('ยังไม่มีประวัติการนำเข้าไฟล์สำหรับโครงการนี้')).toBeInTheDocument(),
    )

    const input = screen.getByLabelText('Primavera P6 (.XER)', { selector: 'input' })
    const file = new File(['xer-bytes'], 'schedule.xer')
    fireEvent.change(input, { target: { files: [file] } })

    await waitFor(() => expect(screen.getByText(/นำเข้าสำเร็จ/)).toBeInTheDocument())
    expect(api.importFile).toHaveBeenCalledWith('project-1', file, 'xer')

    // The history card (DataTable) now shows the row that the upload just created.
    await waitFor(() => expect(screen.getAllByText('schedule.xer').length).toBeGreaterThan(0))
  })

  it('an unknown-project request error is shown without ever touching the history refresh path incorrectly', async () => {
    vi.mocked(api.getImportJobHistory).mockResolvedValueOnce([])
    vi.mocked(api.importFile).mockRejectedValueOnce(
      new api.ImportApiError('ไม่พบโครงการที่ระบุ', 404),
    )

    render(<ImportWizard projectId="project-1" />)
    await waitFor(() =>
      expect(screen.getByText('ยังไม่มีประวัติการนำเข้าไฟล์สำหรับโครงการนี้')).toBeInTheDocument(),
    )

    const input = screen.getByLabelText('Primavera P6 (.XER)', { selector: 'input' })
    fireEvent.change(input, { target: { files: [new File(['x'], 'schedule.xer')] } })

    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent('ไม่พบโครงการที่ระบุ'))
  })
})
