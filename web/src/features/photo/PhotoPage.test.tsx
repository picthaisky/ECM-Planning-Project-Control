import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { PhotoPage } from './PhotoPage'
import { usePhotoOutbox } from './usePhotoOutbox'
import type { OutboxItem } from '../../services/outbox'
import type { PhotoOutboxPayload } from './photoOutbox'

vi.mock('./usePhotoOutbox', () => ({ usePhotoOutbox: vi.fn() }))

function makeItem(overrides: Partial<OutboxItem<PhotoOutboxPayload>> = {}): OutboxItem<PhotoOutboxPayload> {
  return {
    id: 'item-1',
    kind: 'photo',
    idempotencyKey: 'idem-1',
    ownerUserId: 'user-a',
    ownerTenantId: 'tenant-1',
    payload: { projectId: 'project-1', fields: { activityId: null, caption: null, capturedAt: null }, fileName: 'a.jpg' },
    blob: new Blob(['bytes'], { type: 'image/jpeg' }),
    blobFileName: 'a.jpg',
    blobContentType: 'image/jpeg',
    status: 'queued',
    attemptCount: 0,
    lastError: null,
    createdAt: '2026-07-08T09:00:00.000Z',
    updatedAt: '2026-07-08T09:00:00.000Z',
    syncedAt: null,
    serverId: null,
    ...overrides,
  }
}

function renderPage(hookOverrides: Partial<ReturnType<typeof usePhotoOutbox>> = {}) {
  vi.mocked(usePhotoOutbox).mockReturnValue({
    items: [],
    processingState: 'idle',
    processingError: null,
    syncCapability: 'fallback-only',
    capture: vi.fn().mockResolvedValue(true),
    syncNow: vi.fn(),
    reload: vi.fn(),
    ...hookOverrides,
  })

  return render(
    <MemoryRouter initialEntries={['/app/project-1/photo']}>
      <Routes>
        <Route path="/app/:projectId/photo" element={<PhotoPage />} />
      </Routes>
    </MemoryRouter>,
  )
}

describe('PhotoPage', () => {
  it('warns plainly that Background Sync is not available when unsupported, and does not claim automatic sync', () => {
    renderPage({ syncCapability: 'fallback-only' })
    expect(screen.getByText(/ไม่รองรับการซิงค์อัตโนมัติเบื้องหลัง/)).toBeInTheDocument()
    expect(screen.getByText(/ขณะเปิดหน้านี้ค้างไว้/)).toBeInTheDocument()
  })

  it('tells the user sync will happen automatically when Background Sync is supported', () => {
    renderPage({ syncCapability: 'background-sync' })
    expect(screen.getByText(/รองรับการซิงค์อัตโนมัติเบื้องหลัง/)).toBeInTheDocument()
  })

  it('shows a pending-count badge when items are queued/failed', () => {
    renderPage({ items: [makeItem({ status: 'queued' }), makeItem({ id: 'item-2', status: 'failed' })] })
    expect(screen.getByText('รอซิงค์อยู่ 2 รายการ')).toBeInTheDocument()
  })

  it('does not show a pending-count badge when nothing is pending', () => {
    renderPage({ items: [makeItem({ status: 'synced', blob: null })] })
    expect(screen.queryByText(/รอซิงค์อยู่/)).not.toBeInTheDocument()
  })

  it('the "ซิงค์เดี๋ยวนี้" button calls syncNow', async () => {
    const syncNow = vi.fn()
    renderPage({ syncNow })
    await userEvent.click(screen.getByRole('button', { name: 'ซิงค์เดี๋ยวนี้' }))
    expect(syncNow).toHaveBeenCalledTimes(1)
  })
})
