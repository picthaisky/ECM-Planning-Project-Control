import { describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ImportUploadCard } from './ImportUploadCard'
import type { ImportUploadState } from './useImportWizard'
import type { FileImportJob } from './types'

const idleUpload: ImportUploadState = {
  phase: 'idle',
  routeFormat: null,
  fileName: null,
  job: null,
  requestError: null,
}

function baseProps() {
  return {
    upload: idleUpload,
    onSelectFile: vi.fn(),
    onClientValidationError: vi.fn(),
    onReset: vi.fn(),
    onDownloadTemplate: vi.fn(),
    templateDownloadState: 'idle' as const,
    templateDownloadError: null,
  }
}

function getHiddenFileInput(label: string): HTMLInputElement {
  return screen.getByLabelText(label, { selector: 'input' }) as HTMLInputElement
}

describe('ImportUploadCard', () => {
  it('happy path: renders the three prototype import rows', () => {
    render(<ImportUploadCard {...baseProps()} />)

    expect(screen.getByText('Primavera P6 (.XER)')).toBeInTheDocument()
    expect(screen.getByText('Microsoft Project (MSPDI .XML)')).toBeInTheDocument()
    expect(screen.getByText('เทมเพลตความคืบหน้า Excel')).toBeInTheDocument()
    expect(screen.getAllByText('เลือกไฟล์')).toHaveLength(3)
  })

  it('selecting a matching-extension file calls onSelectFile with the routed format', () => {
    const props = baseProps()
    render(<ImportUploadCard {...props} />)

    const input = getHiddenFileInput('Primavera P6 (.XER)')
    const file = new File(['xer-bytes'], 'schedule.xer')
    fireEvent.change(input, { target: { files: [file] } })

    expect(props.onSelectFile).toHaveBeenCalledWith(file, 'xer')
    expect(props.onClientValidationError).not.toHaveBeenCalled()
  })

  it('rejects a wrong-extension file client-side with a Thai message, without calling onSelectFile', () => {
    const props = baseProps()
    render(<ImportUploadCard {...props} />)

    const input = getHiddenFileInput('Primavera P6 (.XER)')
    const file = new File(['not xer'], 'notes.txt')
    fireEvent.change(input, { target: { files: [file] } })

    expect(props.onSelectFile).not.toHaveBeenCalled()
    expect(props.onClientValidationError).toHaveBeenCalledWith(
      expect.stringContaining('นามสกุลไฟล์ไม่ตรงกับประเภทที่เลือก'),
    )
  })

  it('shows an inline progress indicator on the active row while uploading, and disables the others', () => {
    const uploading: ImportUploadState = {
      phase: 'uploading',
      routeFormat: 'xer',
      fileName: 'schedule.xer',
      job: null,
      requestError: null,
    }
    render(<ImportUploadCard {...baseProps()} upload={uploading} />)

    expect(screen.getByText('กำลังอัปโหลด...')).toBeInTheDocument()
    // Every row button (including the busy one) is disabled during upload.
    const rowButtons = screen
      .getAllByRole('button')
      .filter(
        (btn) =>
          btn.textContent?.includes('P6') ||
          btn.textContent?.includes('MSPDI') ||
          btn.textContent?.includes('Excel'),
      )
    expect(rowButtons.length).toBeGreaterThan(0)
    rowButtons.forEach((btn) => expect(btn).toBeDisabled())
  })

  it('shows a Thai success banner with the row count, and "นำเข้าไฟล์อื่น" calls onReset', async () => {
    const succeededJob: FileImportJob = {
      id: 'job-1',
      projectId: 'p1',
      fileName: 'schedule.xer',
      format: 'Xer',
      status: 'Succeeded',
      rowsImported: 128,
      errorJson: null,
      startedAt: '2026-07-27T09:00:00+07:00',
      finishedAt: '2026-07-27T09:00:02+07:00',
      createdByUserId: 'u1',
    }
    const doneUpload: ImportUploadState = {
      phase: 'done',
      routeFormat: 'xer',
      fileName: 'schedule.xer',
      job: succeededJob,
      requestError: null,
    }
    const props = baseProps()
    const user = userEvent.setup()
    render(<ImportUploadCard {...props} upload={doneUpload} />)

    expect(screen.getByText(/นำเข้าสำเร็จ/)).toHaveTextContent('128')
    await user.click(screen.getByRole('button', { name: 'นำเข้าไฟล์อื่น' }))
    expect(props.onReset).toHaveBeenCalledTimes(1)
  })

  it('shows the Thai-translated parser error title and location for a failed job', () => {
    const failedJob: FileImportJob = {
      id: 'job-2',
      projectId: 'p1',
      fileName: 'schedule.xer',
      format: 'Xer',
      status: 'Failed',
      rowsImported: 0,
      errorJson: JSON.stringify({
        code: 'ImportRelationCycleDetected',
        detail: 'A-1010 -> A-1020 -> A-1010',
      }),
      startedAt: '2026-07-27T09:00:00+07:00',
      finishedAt: '2026-07-27T09:00:02+07:00',
      createdByUserId: 'u1',
    }
    const doneUpload: ImportUploadState = {
      phase: 'done',
      routeFormat: 'xer',
      fileName: 'schedule.xer',
      job: failedJob,
      requestError: null,
    }
    render(<ImportUploadCard {...baseProps()} upload={doneUpload} />)

    const banner = screen.getByRole('alert')
    expect(banner).toHaveTextContent('พบการอ้างอิงกิจกรรมแบบวนซ้ำ (Cycle)')
    expect(banner).toHaveTextContent('เส้นทางวนซ้ำ: A-1010 → A-1020 → A-1010')
  })

  it('shows a request-level error (e.g. unknown project) distinctly from a job failure', () => {
    const doneUpload: ImportUploadState = {
      phase: 'done',
      routeFormat: 'xer',
      fileName: 'schedule.xer',
      job: null,
      requestError: 'ไม่พบโครงการที่ระบุ',
    }
    render(<ImportUploadCard {...baseProps()} upload={doneUpload} />)

    expect(screen.getByRole('alert')).toHaveTextContent('ไม่พบโครงการที่ระบุ')
  })

  it('template download: shows a loading label while in flight and a Thai error on failure', () => {
    const { rerender } = render(
      <ImportUploadCard {...baseProps()} templateDownloadState="loading" />,
    )
    expect(screen.getByText('กำลังดาวน์โหลดเทมเพลต...')).toBeInTheDocument()

    rerender(
      <ImportUploadCard
        {...baseProps()}
        templateDownloadState="error"
        templateDownloadError="ดาวน์โหลดเทมเพลตไม่สำเร็จ"
      />,
    )
    expect(screen.getByText('ดาวน์โหลดเทมเพลตไม่สำเร็จ')).toBeInTheDocument()
  })

  it('clicking the template download link calls onDownloadTemplate', async () => {
    const props = baseProps()
    const user = userEvent.setup()
    render(<ImportUploadCard {...props} />)

    await user.click(screen.getByText('ดาวน์โหลดเทมเพลตความคืบหน้า (.xlsx)'))
    expect(props.onDownloadTemplate).toHaveBeenCalledTimes(1)
  })
})
