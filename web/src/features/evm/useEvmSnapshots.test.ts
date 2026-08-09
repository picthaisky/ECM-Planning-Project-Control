import { renderHook, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useEvmSnapshots } from './useEvmSnapshots'
import * as api from './api'
import type { EvmSnapshotDto } from './types'

vi.mock('./api', async () => {
  const actual = await vi.importActual<typeof import('./api')>('./api')
  return { ...actual, listEvmSnapshots: vi.fn() }
})

const sampleSnapshot: EvmSnapshotDto = {
  snapshotId: 'snap-1',
  projectId: 'project-1',
  dataDate: '2026-04-30T00:00:00+07:00',
  bac: '1000000.00',
  pv: '200000.00',
  ev: '180000.00',
  ac: '190000.00',
  eacVariant: 'CpiBased',
  performanceFactor: '1.055556',
  eac: '1055555.56',
  etc: '865555.56',
  vac: '-55555.56',
  createdAt: '2026-05-01T09:00:00+07:00',
}

describe('useEvmSnapshots', () => {
  beforeEach(() => {
    vi.mocked(api.listEvmSnapshots).mockReset()
  })

  it('starts loading, then resolves to ready with the fetched snapshot list, ascending as returned', async () => {
    vi.mocked(api.listEvmSnapshots).mockResolvedValueOnce([sampleSnapshot])

    const { result } = renderHook(() => useEvmSnapshots('project-1'))
    expect(result.current.loadState).toBe('loading')

    await waitFor(() => expect(result.current.loadState).toBe('ready'))
    expect(result.current.snapshots).toEqual([sampleSnapshot])
    expect(api.listEvmSnapshots).toHaveBeenCalledWith('project-1')
  })

  it('goes to error with the Thai EvmApiError message on failure, without crashing the caller', async () => {
    vi.mocked(api.listEvmSnapshots).mockRejectedValueOnce(new api.EvmApiError('โหลดประวัติงวด EVM ไม่สำเร็จ', 500))

    const { result } = renderHook(() => useEvmSnapshots('project-1'))

    await waitFor(() => expect(result.current.loadState).toBe('error'))
    expect(result.current.snapshots).toEqual([])
    expect(result.current.loadError).toBe('โหลดประวัติงวด EVM ไม่สำเร็จ')
  })
})
