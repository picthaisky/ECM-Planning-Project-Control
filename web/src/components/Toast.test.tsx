import { act } from '@testing-library/react'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ToastViewport } from './Toast'
import { useToastStore } from '../store/toastStore'

describe('ToastViewport', () => {
  beforeEach(() => {
    useToastStore.setState({ toasts: [] })
  })

  it('renders nothing when there are no toasts', () => {
    const { container } = render(<ToastViewport />)
    expect(container).toBeEmptyDOMElement()
  })

  it('renders a pushed toast with a status role and a gold dot', () => {
    useToastStore.getState().show({ message: 'เซสชันหมดอายุ กรุณาเข้าสู่ระบบใหม่' })
    render(<ToastViewport />)

    const status = screen.getByRole('status')
    expect(status).toHaveTextContent('เซสชันหมดอายุ')
    expect(status.className).toMatch(/bg-navy/)
    expect(status.querySelector('.bg-gold')).not.toBeNull()
  })

  it('the close button dismisses the toast', async () => {
    useToastStore.getState().show({ message: 'bye' })
    const user = userEvent.setup()
    render(<ToastViewport />)

    await user.click(screen.getByRole('button', { name: 'ปิดการแจ้งเตือน' }))

    expect(useToastStore.getState().toasts).toHaveLength(0)
  })

  describe('auto-dismiss', () => {
    beforeEach(() => {
      vi.useFakeTimers()
    })

    afterEach(() => {
      vi.useRealTimers()
    })

    it('auto-dismisses a toast after the configured timeout', () => {
      useToastStore.getState().show({ message: 'auto' })
      render(<ToastViewport />)
      expect(useToastStore.getState().toasts).toHaveLength(1)

      act(() => {
        vi.advanceTimersByTime(5000)
      })

      expect(useToastStore.getState().toasts).toHaveLength(0)
    })
  })
})
