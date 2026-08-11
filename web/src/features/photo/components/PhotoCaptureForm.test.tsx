import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { PhotoCaptureForm } from './PhotoCaptureForm'

function renderForm(onCapture = vi.fn().mockResolvedValue(true)) {
  render(<PhotoCaptureForm onCapture={onCapture} busy={false} errorMessage={null} />)
  return { onCapture }
}

describe('PhotoCaptureForm', () => {
  it('shows a validation error and never calls onCapture when no file is selected', async () => {
    const { onCapture } = renderForm()
    await userEvent.click(screen.getByRole('button', { name: /บันทึกรูปภาพ/ }))

    expect(screen.getByRole('alert')).toHaveTextContent('กรุณาถ่ายหรือเลือกรูปภาพก่อน')
    expect(onCapture).not.toHaveBeenCalled()
  })

  it('calls onCapture with the selected file and trimmed optional fields', async () => {
    const { onCapture } = renderForm()
    const file = new File(['bytes'], 'site.jpg', { type: 'image/jpeg' })

    await userEvent.upload(screen.getByLabelText('รูปภาพ'), file)
    await userEvent.type(screen.getByLabelText(/คำอธิบายภาพ/), '  เทคอนกรีต  ')
    await userEvent.click(screen.getByRole('button', { name: /บันทึกรูปภาพ/ }))

    expect(onCapture).toHaveBeenCalledTimes(1)
    const [capturedFile, fields] = onCapture.mock.calls[0]
    expect(capturedFile).toBe(file)
    expect(fields.activityId).toBeNull()
    expect(fields.caption).toBe('เทคอนกรีต')
    expect(fields.capturedAt).toEqual(expect.any(String))
  })

  it('rejects a malformed activityId before calling onCapture', async () => {
    const { onCapture } = renderForm()
    const file = new File(['bytes'], 'site.jpg', { type: 'image/jpeg' })

    await userEvent.upload(screen.getByLabelText('รูปภาพ'), file)
    await userEvent.type(screen.getByLabelText(/Activity ID/), 'not-a-guid')
    await userEvent.click(screen.getByRole('button', { name: /บันทึกรูปภาพ/ }))

    expect(screen.getByRole('alert')).toHaveTextContent(/GUID/)
    expect(onCapture).not.toHaveBeenCalled()
  })

  it('clears the selected file and form values after a successful capture', async () => {
    const onCapture = vi.fn().mockResolvedValue(true)
    renderForm(onCapture)
    const file = new File(['bytes'], 'site.jpg', { type: 'image/jpeg' })

    const input = screen.getByLabelText('รูปภาพ') as HTMLInputElement
    await userEvent.upload(input, file)
    expect(screen.getByText(/เลือกแล้ว: site\.jpg/)).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: /บันทึกรูปภาพ/ }))

    expect(screen.queryByText(/เลือกแล้ว:/)).not.toBeInTheDocument()
    expect(input.value).toBe('')
  })

  it('shows the busy label and disables the submit button while compressing', () => {
    render(<PhotoCaptureForm onCapture={vi.fn()} busy errorMessage={null} />)
    expect(screen.getByRole('button', { name: 'กำลังบีบอัดรูปภาพ...' })).toBeDisabled()
  })

  it('surfaces an external error message (e.g. from the outbox hook) as an alert', () => {
    render(<PhotoCaptureForm onCapture={vi.fn()} busy={false} errorMessage="ประมวลผลรูปภาพไม่สำเร็จ" />)
    expect(screen.getByText('ประมวลผลรูปภาพไม่สำเร็จ')).toBeInTheDocument()
  })
})
