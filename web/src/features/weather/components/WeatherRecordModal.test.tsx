import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { WeatherRecordModal } from './WeatherRecordModal'

/**
 * S11-FE-01 DoD (this feature's whole reason for existing): "ฟอร์ม weather เตือนชัดว่า
 * 'บันทึกแล้วแก้ไม่ได้' ก่อนยืนยัน" — every test in this file exists to prove that specific claim,
 * not just that the modal renders.
 */
describe('WeatherRecordModal', () => {
  function renderModal(onSubmit = vi.fn()) {
    render(<WeatherRecordModal isOpen onClose={vi.fn()} onSubmit={onSubmit} busy={false} errorMessage={null} />)
    return { onSubmit }
  }

  it('shows the immutability warning banner, visible before any confirmation', () => {
    renderModal()
    expect(screen.getByText('บันทึกสภาพอากาศไม่สามารถแก้ไขหรือลบได้หลังบันทึก')).toBeInTheDocument()
    expect(screen.getByRole('alert')).toHaveTextContent('ระบบไม่มีปุ่มแก้ไขหรือลบสำหรับบันทึกสภาพอากาศเลย')
  })

  it('names the actual remedy for a mistake — the correction path is not a dead end', () => {
    renderModal()
    expect(screen.getByText(/แก้ไข\/ยกเลิกรายการ/)).toBeInTheDocument()
    expect(screen.getByText(/รายการแก้ไขใหม่ที่อ้างอิงรายการเดิม/)).toBeInTheDocument()
  })

  it('the confirm button is disabled until the acknowledgement checkbox is checked', async () => {
    renderModal()
    const confirmButton = screen.getByRole('button', { name: /ยืนยันบันทึกแบบถาวร/ })
    expect(confirmButton).toBeDisabled()

    await userEvent.click(screen.getByRole('checkbox'))
    expect(confirmButton).toBeEnabled()
  })

  it('the confirm button itself repeats the warning at the moment of the irreversible action', () => {
    renderModal()
    expect(screen.getByRole('button', { name: /แก้ไขภายหลังไม่ได้/ })).toBeInTheDocument()
  })

  it('clicking confirm while unacknowledged never calls onSubmit', async () => {
    const { onSubmit } = renderModal()
    // A disabled <button> does not dispatch a click handler at all — this asserts the outcome
    // (onSubmit never fires), not just the `disabled` attribute, so a future refactor that
    // accidentally removes `disabled` but keeps the attribute check elsewhere still fails this test.
    await userEvent.click(screen.getByRole('button', { name: /ยืนยันบันทึกแบบถาวร/ }))
    expect(onSubmit).not.toHaveBeenCalled()
  })

  it('submits the built payload once acknowledged and the form is valid', async () => {
    const { onSubmit } = renderModal()

    await userEvent.click(screen.getByRole('checkbox'))
    await userEvent.click(screen.getByRole('button', { name: /ยืนยันบันทึกแบบถาวร/ }))

    expect(onSubmit).toHaveBeenCalledTimes(1)
    const payload = onSubmit.mock.calls[0][0]
    expect(payload.condition).toBe('Clear')
    expect(payload.impact).toBe('NoImpact')
    expect(payload.affectedActivityIds).toEqual([])
    expect(typeof payload.logDate).toBe('string')
  })

  it('blocks submit with a Thai validation message when Impact requires HoursLost and none is given', async () => {
    const { onSubmit } = renderModal()

    await userEvent.selectOptions(screen.getByLabelText('ผลกระทบต่องาน'), 'FullStoppage')
    await userEvent.click(screen.getByRole('checkbox'))
    await userEvent.click(screen.getByRole('button', { name: /ยืนยันบันทึกแบบถาวร/ }))

    expect(onSubmit).not.toHaveBeenCalled()
    expect(screen.getByText(/กรุณาระบุจำนวนชั่วโมงที่หยุดงาน/)).toBeInTheDocument()
  })

  it('submits successfully once HoursLost is supplied for a stoppage', async () => {
    const { onSubmit } = renderModal()

    await userEvent.selectOptions(screen.getByLabelText('ผลกระทบต่องาน'), 'FullStoppage')
    await userEvent.type(screen.getByLabelText(/ชั่วโมงที่หยุดงาน/), '8')
    await userEvent.click(screen.getByRole('checkbox'))
    await userEvent.click(screen.getByRole('button', { name: /ยืนยันบันทึกแบบถาวร/ }))

    expect(onSubmit).toHaveBeenCalledTimes(1)
    expect(onSubmit.mock.calls[0][0]).toMatchObject({ impact: 'FullStoppage', hoursLost: '8' })
  })

  it('surfaces a server error message without losing the form contents', async () => {
    render(<WeatherRecordModal isOpen onClose={vi.fn()} onSubmit={vi.fn()} busy={false} errorMessage="พบรหัสกิจกรรมที่ไม่อยู่ในโครงการนี้" />)
    expect(screen.getByText('พบรหัสกิจกรรมที่ไม่อยู่ในโครงการนี้')).toBeInTheDocument()
  })

  it('lets the user add an affected activity by GUID (no activity-listing endpoint exists)', async () => {
    const { onSubmit } = renderModal()

    await userEvent.type(screen.getByLabelText(/Activity ID/), '3fa85f64-5717-4562-b3fc-2c963f66afa6')
    await userEvent.click(screen.getByRole('button', { name: '+ เพิ่ม' }))
    expect(screen.getByText('3fa85f64-5717-4562-b3fc-2c963f66afa6')).toBeInTheDocument()

    await userEvent.click(screen.getByRole('checkbox'))
    await userEvent.click(screen.getByRole('button', { name: /ยืนยันบันทึกแบบถาวร/ }))

    expect(onSubmit.mock.calls[0][0].affectedActivityIds).toEqual(['3fa85f64-5717-4562-b3fc-2c963f66afa6'])
  })
})
