import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { PhotoOutboxList } from './PhotoOutboxList'
import type { OutboxItem } from '../../../services/outbox'
import type { PhotoOutboxPayload } from '../photoOutbox'

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

describe('PhotoOutboxList', () => {
  it('shows an empty-state message when the queue is empty', () => {
    render(<PhotoOutboxList items={[]} onRetry={vi.fn()} />)
    expect(screen.getByText('ยังไม่มีรูปภาพในคิวของอุปกรณ์นี้')).toBeInTheDocument()
  })

  it('shows the Thai "รอซิงค์" status for a queued item (S12-FE-01 DoD)', () => {
    render(<PhotoOutboxList items={[makeItem({ status: 'queued' })]} onRetry={vi.fn()} />)
    expect(screen.getByText('รอซิงค์')).toBeInTheDocument()
  })

  it('shows the Thai "ซิงค์แล้ว" status once synced (S12-FE-01 DoD)', () => {
    render(<PhotoOutboxList items={[makeItem({ status: 'synced', serverId: 'server-1', blob: null })]} onRetry={vi.fn()} />)
    expect(screen.getByText('ซิงค์แล้ว')).toBeInTheDocument()
  })

  it('shows "กำลังซิงค์..." while an upload is in flight', () => {
    render(<PhotoOutboxList items={[makeItem({ status: 'syncing' })]} onRetry={vi.fn()} />)
    expect(screen.getByText('กำลังซิงค์...')).toBeInTheDocument()
  })

  it('shows the failure reason and a retry action for a failed item', async () => {
    const onRetry = vi.fn()
    render(
      <PhotoOutboxList
        items={[makeItem({ status: 'failed', lastError: 'เครือข่ายขัดข้อง', attemptCount: 1 })]}
        onRetry={onRetry}
      />,
    )

    expect(screen.getByText('ซิงค์ไม่สำเร็จ')).toBeInTheDocument()
    expect(screen.getByText('เครือข่ายขัดข้อง')).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'ลองซิงค์ใหม่' }))
    expect(onRetry).toHaveBeenCalledTimes(1)
  })

  it('shows the caption and activityId when present', () => {
    render(
      <PhotoOutboxList
        items={[
          makeItem({
            payload: {
              projectId: 'project-1',
              fields: { activityId: '3fa85f64-5717-4562-b3fc-2c963f66afa6', caption: 'เทคอนกรีตพื้นชั้น 9', capturedAt: null },
              fileName: 'a.jpg',
            },
          }),
        ]}
        onRetry={vi.fn()}
      />,
    )

    expect(screen.getByText('เทคอนกรีตพื้นชั้น 9')).toBeInTheDocument()
    expect(screen.getByText(/3fa85f64-5717-4562-b3fc-2c963f66afa6/)).toBeInTheDocument()
  })

  it('does not show a retry action for a queued (not-yet-attempted) item', () => {
    render(<PhotoOutboxList items={[makeItem({ status: 'queued' })]} onRetry={vi.fn()} />)
    expect(screen.queryByRole('button', { name: 'ลองซิงค์ใหม่' })).not.toBeInTheDocument()
  })
})
