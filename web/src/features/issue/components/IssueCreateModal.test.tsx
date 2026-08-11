import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { IssueCreateModal } from './IssueCreateModal'

describe('IssueCreateModal', () => {
  it('requires a title before submitting', async () => {
    const onSubmit = vi.fn()
    render(<IssueCreateModal isOpen onClose={vi.fn()} onSubmit={onSubmit} busy={false} errorMessage={null} />)

    await userEvent.click(screen.getByRole('button', { name: 'บันทึก' }))

    expect(onSubmit).not.toHaveBeenCalled()
    expect(screen.getByText('กรุณาระบุหัวข้อปัญหา')).toBeInTheDocument()
  })

  it('submits with optional fields converted to null when left blank', async () => {
    const onSubmit = vi.fn()
    render(<IssueCreateModal isOpen onClose={vi.fn()} onSubmit={onSubmit} busy={false} errorMessage={null} />)

    await userEvent.type(screen.getByLabelText(/หัวข้อปัญหา/), 'น้ำรั่วซึมผนัง Basement โซน B')
    await userEvent.click(screen.getByRole('button', { name: 'บันทึก' }))

    expect(onSubmit).toHaveBeenCalledWith({
      title: 'น้ำรั่วซึมผนัง Basement โซน B',
      detail: null,
      owner: null,
      dueDate: null,
    })
  })

  it('submits the full payload with detail/owner/dueDate when filled in', async () => {
    const onSubmit = vi.fn()
    render(<IssueCreateModal isOpen onClose={vi.fn()} onSubmit={onSubmit} busy={false} errorMessage={null} />)

    await userEvent.type(screen.getByLabelText(/หัวข้อปัญหา/), 'เหล็กเส้นส่งช้า')
    await userEvent.type(screen.getByLabelText(/รายละเอียด/), 'ซัพพลายเออร์แจ้งเลื่อน 5 วัน')
    await userEvent.type(screen.getByLabelText(/ผู้รับผิดชอบ/), 'จัดซื้อ')
    await userEvent.type(screen.getByLabelText(/กำหนดแก้ไข/), '2026-07-18')
    await userEvent.click(screen.getByRole('button', { name: 'บันทึก' }))

    expect(onSubmit).toHaveBeenCalledWith({
      title: 'เหล็กเส้นส่งช้า',
      detail: 'ซัพพลายเออร์แจ้งเลื่อน 5 วัน',
      owner: 'จัดซื้อ',
      dueDate: '2026-07-18T00:00:00.000Z',
    })
  })

  it('shows a server error message', () => {
    render(<IssueCreateModal isOpen onClose={vi.fn()} onSubmit={vi.fn()} busy={false} errorMessage="แจ้งปัญหาใหม่ไม่สำเร็จ" />)
    expect(screen.getByText('แจ้งปัญหาใหม่ไม่สำเร็จ')).toBeInTheDocument()
  })
})
