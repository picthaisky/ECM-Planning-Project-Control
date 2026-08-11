import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { CaptureBaselineModal } from './CaptureBaselineModal'

describe('CaptureBaselineModal', () => {
  it('requires a non-empty name before calling onCapture', async () => {
    const onCapture = vi.fn()
    const user = userEvent.setup()
    render(<CaptureBaselineModal isOpen onClose={vi.fn()} busy={false} errorMessage={null} onCapture={onCapture} />)

    await user.click(screen.getByRole('button', { name: 'บันทึก Baseline' }))

    expect(screen.getByText('จำเป็นต้องกรอกชื่อ Baseline')).toBeInTheDocument()
    expect(onCapture).not.toHaveBeenCalled()
  })

  it('calls onCapture with the trimmed name', async () => {
    const onCapture = vi.fn()
    const user = userEvent.setup()
    render(<CaptureBaselineModal isOpen onClose={vi.fn()} busy={false} errorMessage={null} onCapture={onCapture} />)

    await user.type(screen.getByLabelText('ชื่อ Baseline'), '  Baseline 1  ')
    await user.click(screen.getByRole('button', { name: 'บันทึก Baseline' }))

    expect(onCapture).toHaveBeenCalledWith('Baseline 1')
  })

  it('shows the server error and keeps the form open', () => {
    render(
      <CaptureBaselineModal isOpen onClose={vi.fn()} busy={false} errorMessage="บันทึกไม่สำเร็จ" onCapture={vi.fn()} />,
    )
    expect(screen.getByText('บันทึกไม่สำเร็จ')).toBeInTheDocument()
  })

  it('renders nothing when closed', () => {
    render(<CaptureBaselineModal isOpen={false} onClose={vi.fn()} busy={false} errorMessage={null} onCapture={vi.fn()} />)
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })
})
