import { act, renderHook, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useWbsTree } from './useWbsTree'
import * as api from './api'
import type { WbsTreeDto } from './types'

vi.mock('./api', async () => {
  const actual = await vi.importActual<typeof import('./api')>('./api')
  return { ...actual, getWbsTreeWithCacheInfo: vi.fn() }
})

const sampleTree: WbsTreeDto = {
  projectId: 'project-1',
  rootNodes: [
    {
      id: 'root-1',
      parentWbsNodeId: null,
      code: '1',
      title: 'งานโครงสร้าง',
      weightPercentage: '38.00',
      activityCount: 0,
      children: [
        {
          id: 'child-1',
          parentWbsNodeId: 'root-1',
          code: '1.1',
          title: 'เสาเข็ม',
          weightPercentage: '10.00',
          activityCount: 4,
          children: [],
        },
      ],
    },
  ],
}

describe('useWbsTree', () => {
  beforeEach(() => {
    vi.mocked(api.getWbsTreeWithCacheInfo).mockReset()
  })

  it('loads the tree on mount and flattens to root-only rows by default', async () => {
    vi.mocked(api.getWbsTreeWithCacheInfo).mockResolvedValueOnce({ tree: sampleTree, servedFromOfflineCache: false })
    const { result } = renderHook(() => useWbsTree('project-1'))

    expect(result.current.loadState).toBe('loading')
    await waitFor(() => expect(result.current.loadState).toBe('ready'))
    expect(result.current.rows.map((r) => r.node.code)).toEqual(['1'])
  })

  it('toggleNode expands a node to reveal its children', async () => {
    vi.mocked(api.getWbsTreeWithCacheInfo).mockResolvedValueOnce({ tree: sampleTree, servedFromOfflineCache: false })
    const { result } = renderHook(() => useWbsTree('project-1'))
    await waitFor(() => expect(result.current.loadState).toBe('ready'))

    act(() => result.current.toggleNode('root-1'))
    expect(result.current.rows.map((r) => r.node.code)).toEqual(['1', '1.1'])

    act(() => result.current.toggleNode('root-1'))
    expect(result.current.rows.map((r) => r.node.code)).toEqual(['1'])
  })

  it('expandAll/collapseAll toggle every node at once', async () => {
    vi.mocked(api.getWbsTreeWithCacheInfo).mockResolvedValueOnce({ tree: sampleTree, servedFromOfflineCache: false })
    const { result } = renderHook(() => useWbsTree('project-1'))
    await waitFor(() => expect(result.current.loadState).toBe('ready'))

    act(() => result.current.expandAll())
    expect(result.current.rows).toHaveLength(2)

    act(() => result.current.collapseAll())
    expect(result.current.rows).toHaveLength(1)
  })

  it('surfaces a Thai error state on load failure', async () => {
    vi.mocked(api.getWbsTreeWithCacheInfo).mockRejectedValueOnce(new api.WbsApiError('ไม่พบโครงการที่ระบุ', 404))
    const { result } = renderHook(() => useWbsTree('project-1'))

    await waitFor(() => expect(result.current.loadState).toBe('error'))
    expect(result.current.loadError).toBe('ไม่พบโครงการที่ระบุ')
  })

  describe('S13-FE-02: offline-cache badge', () => {
    it('defaults servedFromOfflineCache to false for a live response', async () => {
      vi.mocked(api.getWbsTreeWithCacheInfo).mockResolvedValueOnce({ tree: sampleTree, servedFromOfflineCache: false })
      const { result } = renderHook(() => useWbsTree('project-1'))
      await waitFor(() => expect(result.current.loadState).toBe('ready'))

      expect(result.current.servedFromOfflineCache).toBe(false)
    })

    it('surfaces servedFromOfflineCache: true when the service worker served this response from its runtime cache', async () => {
      vi.mocked(api.getWbsTreeWithCacheInfo).mockResolvedValueOnce({ tree: sampleTree, servedFromOfflineCache: true })
      const { result } = renderHook(() => useWbsTree('project-1'))
      await waitFor(() => expect(result.current.loadState).toBe('ready'))

      expect(result.current.servedFromOfflineCache).toBe(true)
    })
  })
})
