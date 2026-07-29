import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Button } from './Button'

describe('Button', () => {
  it('renders its label and defaults to variant=primary, type=button', () => {
    render(<Button>บันทึก</Button>)
    const button = screen.getByRole('button', { name: 'บันทึก' })
    expect(button).toHaveAttribute('type', 'button')
    expect(button.className).toMatch(/bg-navy/)
  })

  it('applies secondary variant classes', () => {
    render(<Button variant="secondary">ยกเลิก</Button>)
    expect(screen.getByRole('button', { name: 'ยกเลิก' }).className).toMatch(/text-navy/)
  })

  it('applies danger variant classes', () => {
    render(<Button variant="danger">ลบ</Button>)
    expect(screen.getByRole('button', { name: 'ลบ' }).className).toMatch(/bg-danger/)
  })

  it('fires onClick when enabled', async () => {
    const onClick = vi.fn()
    const user = userEvent.setup()
    render(<Button onClick={onClick}>ยืนยัน</Button>)
    await user.click(screen.getByRole('button', { name: 'ยืนยัน' }))
    expect(onClick).toHaveBeenCalledTimes(1)
  })

  it('disabled state: does not fire onClick and exposes disabled', async () => {
    const onClick = vi.fn()
    const user = userEvent.setup()
    render(
      <Button disabled onClick={onClick}>
        ปิด
      </Button>,
    )
    const button = screen.getByRole('button', { name: 'ปิด' })
    expect(button).toBeDisabled()
    await user.click(button)
    expect(onClick).not.toHaveBeenCalled()
  })

  it('loading state: shows a spinner, marks aria-busy, and blocks clicks', async () => {
    const onClick = vi.fn()
    const user = userEvent.setup()
    render(
      <Button loading onClick={onClick}>
        กำลังบันทึก
      </Button>,
    )
    const button = screen.getByRole('button', { name: /กำลังบันทึก/ })
    expect(button).toBeDisabled()
    expect(button).toHaveAttribute('aria-busy', 'true')
    expect(screen.getByTestId('button-spinner')).toBeInTheDocument()
    await user.click(button)
    expect(onClick).not.toHaveBeenCalled()
  })
})
