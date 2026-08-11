import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ApprovalActionModal } from './ApprovalActionModal'

describe('ApprovalActionModal', () => {
  it('approve: comment is optional — confirm is enabled with an empty comment', async () => {
    const onConfirm = vi.fn()
    render(
      <ApprovalActionModal
        kind="approve"
        isOpen
        onCancel={vi.fn()}
        onConfirm={onConfirm}
        busy={false}
        errorMessage={null}
      />,
    )

    await userEvent.click(screen.getByRole('button', { name: 'อนุมัติ' }))
    expect(onConfirm).toHaveBeenCalledWith('')
  })

  it('return: confirm is disabled until a non-blank comment is entered, and the copy says "ไม่ใช่การปฏิเสธ"', async () => {
    const onConfirm = vi.fn()
    render(
      <ApprovalActionModal
        kind="return"
        isOpen
        onCancel={vi.fn()}
        onConfirm={onConfirm}
        busy={false}
        errorMessage={null}
      />,
    )

    expect(screen.getByText(/ไม่ใช่การปฏิเสธ/)).toBeInTheDocument()
    const confirmButton = screen.getByRole('button', { name: 'ยืนยันตีกลับ' })
    expect(confirmButton).toBeDisabled()

    await userEvent.type(screen.getByPlaceholderText('เหตุผลที่ตีกลับ (บังคับกรอก)'), 'ยอดไม่ตรงกับ BOQ')
    expect(confirmButton).toBeEnabled()

    await userEvent.click(confirmButton)
    expect(onConfirm).toHaveBeenCalledWith('ยอดไม่ตรงกับ BOQ')
  })

  it('reject: requires a comment, is styled as a dangerous/terminal action, and says it cannot be undone', async () => {
    const onConfirm = vi.fn()
    render(
      <ApprovalActionModal
        kind="reject"
        isOpen
        onCancel={vi.fn()}
        onConfirm={onConfirm}
        busy={false}
        errorMessage={null}
      />,
    )

    expect(screen.getByText(/ไม่สามารถย้อนกลับ/)).toBeInTheDocument()
    const confirmButton = screen.getByRole('button', { name: 'ยืนยันปฏิเสธ' })
    expect(confirmButton).toBeDisabled()

    await userEvent.type(screen.getByPlaceholderText('เหตุผลที่ปฏิเสธ (บังคับกรอก)'), 'ยกเลิกงวดนี้ถาวร')
    expect(confirmButton).toBeEnabled()
    await userEvent.click(confirmButton)
    expect(onConfirm).toHaveBeenCalledWith('ยกเลิกงวดนี้ถาวร')
  })

  it('whitespace-only comment is treated as blank (trimmed) for a required-comment action', async () => {
    render(
      <ApprovalActionModal
        kind="reject"
        isOpen
        onCancel={vi.fn()}
        onConfirm={vi.fn()}
        busy={false}
        errorMessage={null}
      />,
    )

    await userEvent.type(screen.getByPlaceholderText('เหตุผลที่ปฏิเสธ (บังคับกรอก)'), '   ')
    expect(screen.getByRole('button', { name: 'ยืนยันปฏิเสธ' })).toBeDisabled()
  })

  it('shows the server error message and re-enables cancel while not busy', () => {
    render(
      <ApprovalActionModal
        kind="approve"
        isOpen
        onCancel={vi.fn()}
        onConfirm={vi.fn()}
        busy={false}
        errorMessage="คุณไม่มีสิทธิ์ดำเนินการกับขั้นตอนอนุมัติปัจจุบันของเอกสารนี้"
      />,
    )

    expect(screen.getByRole('alert')).toHaveTextContent('คุณไม่มีสิทธิ์ดำเนินการกับขั้นตอนอนุมัติปัจจุบันของเอกสารนี้')
  })

  it('disables cancel and shows a loading confirm button while busy', () => {
    render(
      <ApprovalActionModal
        kind="approve"
        isOpen
        onCancel={vi.fn()}
        onConfirm={vi.fn()}
        busy
        errorMessage={null}
      />,
    )

    expect(screen.getByRole('button', { name: 'ยกเลิก' })).toBeDisabled()
  })

  it('empty state: renders nothing when isOpen is false', () => {
    render(
      <ApprovalActionModal
        kind="approve"
        isOpen={false}
        onCancel={vi.fn()}
        onConfirm={vi.fn()}
        busy={false}
        errorMessage={null}
      />,
    )
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })
})
