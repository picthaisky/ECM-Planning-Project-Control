import { describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Modal } from './Modal'

describe('Modal', () => {
  it('empty state: renders nothing when isOpen is false', () => {
    render(
      <Modal isOpen={false} onClose={vi.fn()} title="ยืนยันการอนุมัติ">
        เนื้อหา
      </Modal>,
    )
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('renders the dialog with title and content when open', () => {
    render(
      <Modal isOpen onClose={vi.fn()} title="ยืนยันการอนุมัติ">
        เนื้อหา
      </Modal>,
    )
    const dialog = screen.getByRole('dialog')
    expect(dialog).toHaveAttribute('aria-modal', 'true')
    expect(screen.getByText('ยืนยันการอนุมัติ')).toBeInTheDocument()
    expect(screen.getByText('เนื้อหา')).toBeInTheDocument()
  })

  it('calls onClose on Escape', () => {
    const onClose = vi.fn()
    render(
      <Modal isOpen onClose={onClose} title="ปิดด้วย Escape">
        เนื้อหา
      </Modal>,
    )
    fireEvent.keyDown(document, { key: 'Escape' })
    expect(onClose).toHaveBeenCalledTimes(1)
  })

  it('calls onClose on backdrop click but not on click inside the panel', async () => {
    const onClose = vi.fn()
    const user = userEvent.setup()
    render(
      <Modal isOpen onClose={onClose} title="แบ็คดรอป">
        <button type="button">อยู่ในโมดัล</button>
      </Modal>,
    )

    await user.click(screen.getByText('อยู่ในโมดัล'))
    expect(onClose).not.toHaveBeenCalled()

    // The dialog's direct parent is the fixed backdrop overlay.
    const backdrop = screen.getByRole('dialog').parentElement as HTMLElement
    fireEvent.mouseDown(backdrop)
    expect(onClose).toHaveBeenCalledTimes(1)
  })

  it('traps focus: Tab from the last focusable wraps to the first, Shift+Tab from the first wraps to the last', async () => {
    const user = userEvent.setup()
    render(
      <Modal isOpen onClose={vi.fn()} title="โฟกัส">
        <button type="button">แรก</button>
        <button type="button">สุดท้าย</button>
      </Modal>,
    )

    const first = screen.getByText('แรก')
    const last = screen.getByText('สุดท้าย')

    expect(first).toHaveFocus()

    await user.tab()
    expect(last).toHaveFocus()

    await user.tab()
    expect(first).toHaveFocus()

    await user.tab({ shift: true })
    expect(last).toHaveFocus()
  })
})
