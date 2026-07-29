import { act, renderHook, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useWbsTree } from './useWbsTree'
import * as api from './api'
import type { WbsTreeDto } from './types'

vi.mock('./api', async () => {
  const actual = await vi.importActual<typeof import('./api')>('./api')
  return { ...actual, getWbsTree: vi.fn() }
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
    vi.mocked(api.getWbsTree).mockReset()
  })

  it('loads the tree on mount and flattens to root-only rows by default', async () => {
    vi.mocked(api.getWbsTree).mockResolvedValueOnce(sampleTree)
    const { result } = renderHook(() => useWbsTree('project-1'))

    expect(result.current.loadState).toBe('loading')
    await waitFor(() => expect(result.current.loadState).toBe('ready'))
    expect(result.current.rows.map((r) => r.node.code)).toEqual(['1'])
  })

  it('toggleNode expands a node to reveal its children', async () => {
    vi.mocked(api.getWbsTree).mockResolvedValueOnce(sampleTree)
    const { result } = renderHook(() => useWbsTree('project-1'))
    await waitFor(() => expect(result.current.loadState).toBe('ready'))

    act(() => result.current.toggleNode('root-1'))
    expect(result.current.rows.map((r) => r.node.code)).toEqual(['1', '1.1'])

    act(() => result.current.toggleNode('root-1'))
    expect(result.current.rows.map((r) => r.node.code)).toEqual(['1'])
  })

  it('expandAll/collapseAll toggle every node at once', async () => {
    vi.mocked(api.getWbsTree).mockResolvedValueOnce(sampleTree)
    const { result } = renderHook(() => useWbsTree('project-1'))
    await waitFor(() => expect(result.current.loadState).toBe('ready'))

    act(() => result.current.expandAll())
    expect(result.current.rows).toHaveLength(2)

    act(() => result.current.collapseAll())
    expect(result.current.rows).toHaveLength(1)
  })

  it('surfaces a Thai error state on load failure', async () => {
    vi.mocked(api.getWbsTree).mockRejectedValueOnce(new api.WbsApiError('ไม่พบโครงการที่ระบุ', 404))
    const { result } = renderHook(() => useWbsTree('project-1'))

    await waitFor(() => expect(result.current.loadState).toBe('error'))
    expect(result.current.loadError).toBe('ไม่พบโครงการที่ระบุ')
  })
})
