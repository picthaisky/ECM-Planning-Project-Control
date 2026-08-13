import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ManpowerRecordModal } from './ManpowerRecordModal'

const VALID_GUID = '3fa85f64-5717-4562-b3fc-2c963f66afa6'

function renderModal(onSubmit = vi.fn()) {
  render(<ManpowerRecordModal isOpen projectId="p1" onClose={vi.fn()} onSubmit={onSubmit} busy={false} errorMessage={null} />)
  return { onSubmit }
}

async function fillMinimumValidForm() {
  await userEvent.type(screen.getByLabelText(/Work Category/), VALID_GUID)
  await userEvent.clear(screen.getByLabelText('จำนวนคน (Worker Count)'))
  await userEvent.type(screen.getByLabelText('จำนวนคน (Worker Count)'), '25')
  await userEvent.clear(screen.getByLabelText('ชั่วโมงแรงงานรวม (Man-Hours)'))
  await userEvent.type(screen.getByLabelText('ชั่วโมงแรงงานรวม (Man-Hours)'), '200')
}

describe('ManpowerRecordModal', () => {
  it('shows the append-only notice', () => {
    renderModal()
    expect(screen.getByText(/แก้ไข\/ลบไม่ได้หลังบันทึก/)).toBeInTheDocument()
  })

  it('blocks submit and shows a validation error when required fields are missing', async () => {
    const { onSubmit } = renderModal()
    await userEvent.click(screen.getByRole('button', { name: 'บันทึก' }))

    expect(screen.getByRole('alert')).toBeInTheDocument()
    expect(onSubmit).not.toHaveBeenCalled()
  })

  it('submits a well-formed payload with allowDuplicate defaulting to false', async () => {
    const { onSubmit } = renderModal()
    await fillMinimumValidForm()
    await userEvent.click(screen.getByRole('button', { name: 'บันทึก' }))

    expect(onSubmit).toHaveBeenCalledTimes(1)
    const payload = onSubmit.mock.calls[0][0]
    expect(payload.workCategoryId).toBe(VALID_GUID)
    expect(payload.workerCount).toBe(25)
    expect(payload.manHours).toBe('200')
    expect(payload.allowDuplicate).toBe(false)
  })

  it('submits allowDuplicate: true once the checkbox is ticked', async () => {
    const { onSubmit } = renderModal()
    await fillMinimumValidForm()
    await userEvent.click(screen.getByRole('checkbox', { name: /ยืนยันบันทึกซ้ำ/ }))
    await userEvent.click(screen.getByRole('button', { name: 'บันทึก' }))

    expect(onSubmit.mock.calls[0][0].allowDuplicate).toBe(true)
  })

  it('surfaces an external errorMessage (e.g. 409 from the server) as an alert', () => {
    render(
      <ManpowerRecordModal
        isOpen
        projectId="p1"
        onClose={vi.fn()}
        onSubmit={vi.fn()}
        busy={false}
        errorMessage="มีบันทึกสำหรับวันที่/กะ/หมวดงาน/WBS Node/ประเภทแรงงานนี้อยู่แล้ว"
      />,
    )
    expect(screen.getByText(/มีบันทึกสำหรับวันที่/)).toBeInTheDocument()
  })

  it('shows subcontractor field only when labour type is not OwnDirect', async () => {
    renderModal()
    expect(screen.queryByLabelText(/Subcontractor Ref/)).not.toBeInTheDocument()

    await userEvent.selectOptions(screen.getByLabelText('ประเภทแรงงาน'), 'Subcontract')
    expect(screen.getByLabelText(/Subcontractor Ref/)).toBeInTheDocument()
  })
})
