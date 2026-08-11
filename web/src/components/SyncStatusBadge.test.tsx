import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { SyncStatusBadge } from './SyncStatusBadge'
import type { OutboxSyncStatus } from '../services/useOutboxSyncStatus'
import type { OutboxItem } from '../services/outbox'

function makeItem(overrides: Partial<OutboxItem> = {}): OutboxItem {
  return {
    id: 'item-1',
    kind: 'photo',
    idempotencyKey: 'idem-1',
    ownerUserId: 'user-1',
    ownerTenantId: 'tenant-1',
    payload: {},
    blob: null,
    blobFileName: null,
    blobContentType: null,
    status: 'queued',
    attemptCount: 0,
    lastError: null,
    createdAt: '2026-08-11T09:00:00.000Z',
    updatedAt: '2026-08-11T09:00:00.000Z',
    syncedAt: null,
    serverId: null,
    ...overrides,
  }
}

function makeStatus(overrides: Partial<OutboxSyncStatus> = {}): OutboxSyncStatus {
  return {
    items: [],
    pendingCount: 0,
    failedCount: 0,
    conflictCount: 0,
    pendingBlobCount: 0,
    syncCapability: 'fallback-only',
    isSyncing: false,
    syncNow: vi.fn().mockResolvedValue(undefined),
    reload: vi.fn().mockResolvedValue(undefined),
    ...overrides,
  }
}

describe('SyncStatusBadge (S13-FE-03)', () => {
  it('shows "ซิงค์ครบแล้ว" and no popover trigger content when nothing is pending or in conflict', () => {
    render(<SyncStatusBadge status={makeStatus()} />)
    expect(screen.getByText('ซิงค์ครบแล้ว')).toBeInTheDocument()
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('shows the pending count, and a distinct attention badge combining failed + conflict', () => {
    render(
      <SyncStatusBadge
        status={makeStatus({
          items: [makeItem({ status: 'queued' }), makeItem({ id: 'i2', status: 'failed' }), makeItem({ id: 'i3', status: 'conflict' })],
          pendingCount: 2,
          failedCount: 1,
          conflictCount: 1,
        })}
      />,
    )

    expect(screen.getByText('รอซิงค์ 2 รายการ')).toBeInTheDocument()
    expect(screen.getByText('2')).toBeInTheDocument() // failedCount + conflictCount
  })

  it('clicking the toggle opens a panel listing each non-synced item with its Thai status/error', async () => {
    render(
      <SyncStatusBadge
        status={makeStatus({
          items: [
            makeItem({ id: 'i1', kind: 'weather-log', status: 'failed', lastError: 'เครือข่ายขัดข้อง' }),
            makeItem({ id: 'i2', kind: 'progress-batch', status: 'conflict', lastError: 'ข้อมูลขัดแย้งกับรายการที่เคยส่งไปแล้ว' }),
          ],
          pendingCount: 1,
          failedCount: 1,
          conflictCount: 1,
        })}
      />,
    )

    await userEvent.click(screen.getByTestId('sync-status-badge-toggle'))

    expect(screen.getByRole('dialog', { name: 'สถานะการซิงค์ทั้งระบบ' })).toBeInTheDocument()
    const items = screen.getAllByTestId('sync-status-item')
    expect(items).toHaveLength(2)
    expect(screen.getByText('บันทึกสภาพอากาศ')).toBeInTheDocument()
    expect(screen.getByText('ความคืบหน้า (batch)')).toBeInTheDocument()
    expect(screen.getByText('เครือข่ายขัดข้อง')).toBeInTheDocument()
    expect(screen.getByText('ข้อมูลขัดแย้งกับรายการที่เคยส่งไปแล้ว')).toBeInTheDocument()
  })

  it('never claims automatic background sync where the device does not support it (fallback-only copy)', async () => {
    render(<SyncStatusBadge status={makeStatus({ items: [makeItem()], pendingCount: 1, syncCapability: 'fallback-only' })} />)
    await userEvent.click(screen.getByTestId('sync-status-badge-toggle'))
    expect(screen.getByText(/ไม่รองรับการซิงค์อัตโนมัติเบื้องหลัง/)).toBeInTheDocument()
  })

  it('shows the background-sync-capable copy when the device supports it', async () => {
    render(<SyncStatusBadge status={makeStatus({ items: [makeItem()], pendingCount: 1, syncCapability: 'background-sync' })} />)
    await userEvent.click(screen.getByTestId('sync-status-badge-toggle'))
    expect(screen.getByText(/รองรับการซิงค์อัตโนมัติเบื้องหลัง \(Background Sync\)/)).toBeInTheDocument()
  })

  it('"ซิงค์ทั้งหมดตอนนี้" calls syncNow()', async () => {
    const syncNow = vi.fn().mockResolvedValue(undefined)
    render(<SyncStatusBadge status={makeStatus({ items: [makeItem()], pendingCount: 1, syncNow })} />)
    await userEvent.click(screen.getByTestId('sync-status-badge-toggle'))

    await userEvent.click(screen.getByRole('button', { name: 'ซิงค์ทั้งหมดตอนนี้' }))

    expect(syncNow).toHaveBeenCalledTimes(1)
  })
})
