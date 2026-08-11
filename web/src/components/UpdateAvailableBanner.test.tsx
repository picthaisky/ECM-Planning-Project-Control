import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { UpdateAvailableBanner } from './UpdateAvailableBanner'
import { useSwUpdateStore } from '../store/swUpdateStore'
import * as registerServiceWorkerModule from '../services/registerServiceWorker'

vi.mock('../services/registerServiceWorker', async () => {
  const actual = await vi.importActual<typeof import('../services/registerServiceWorker')>(
    '../services/registerServiceWorker',
  )
  return { ...actual, activatePendingUpdate: vi.fn() }
})

describe('UpdateAvailableBanner (S13-FE-02)', () => {
  beforeEach(() => {
    useSwUpdateStore.setState({ updateAvailable: false })
    vi.mocked(registerServiceWorkerModule.activatePendingUpdate).mockReset()
  })

  it('renders nothing when no update is available', () => {
    render(<UpdateAvailableBanner />)
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('shows the persistent banner once an update is available', () => {
    useSwUpdateStore.setState({ updateAvailable: true })
    render(<UpdateAvailableBanner />)

    expect(screen.getByRole('alert')).toHaveTextContent('มีอัปเดตใหม่ของระบบพร้อมใช้งาน')
    expect(screen.getByRole('button', { name: 'โหลดหน้าใหม่เพื่ออัปเดต' })).toBeInTheDocument()
  })

  it('clicking the reload button calls activatePendingUpdate()', async () => {
    useSwUpdateStore.setState({ updateAvailable: true })
    render(<UpdateAvailableBanner />)

    await userEvent.click(screen.getByRole('button', { name: 'โหลดหน้าใหม่เพื่ออัปเดต' }))

    expect(registerServiceWorkerModule.activatePendingUpdate).toHaveBeenCalledTimes(1)
  })
})
