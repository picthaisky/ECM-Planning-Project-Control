import { useCallback, useEffect, useMemo, useState } from 'react'
import { getWbsTree, WbsApiError } from './api'
import { flattenWbsTree } from './flattenTree'
import type { WbsTreeNodeDto } from './types'

export type WbsTreeLoadState = 'loading' | 'ready' | 'error'

function collectAllNodeIds(nodes: readonly WbsTreeNodeDto[]): string[] {
  const ids: string[] = []
  const stack = [...nodes]
  while (stack.length > 0) {
    const n = stack.pop()!
    ids.push(n.id)
    stack.push(...n.children)
  }
  return ids
}

/**
 * Loads the real WBS tree (`GET .../wbs-tree`, S4-BE-01) and drives the flattened, virtualized grid
 * (`WbsTreeGrid`): expand/collapse state per node, "ขยายทั้งหมด"/"ย่อทั้งหมด" (matches the
 * prototype's own two buttons), and the flattened row list (`flattenTree.ts`) recomputed whenever
 * either the tree or the expand set changes.
 */
export function useWbsTree(projectId: string) {
  const [rootNodes, setRootNodes] = useState<WbsTreeNodeDto[]>([])
  const [loadState, setLoadState] = useState<WbsTreeLoadState>('loading')
  const [loadError, setLoadError] = useState<string | null>(null)
  const [expandedIds, setExpandedIds] = useState<Set<string>>(new Set())

  const load = useCallback(async () => {
    setLoadState('loading')
    setLoadError(null)
    try {
      const tree = await getWbsTree(projectId)
      setRootNodes(tree.rootNodes)
      setLoadState('ready')
    } catch (error) {
      setLoadState('error')
      setLoadError(error instanceof WbsApiError ? error.message : 'โหลดข้อมูล WBS ไม่สำเร็จ')
    }
  }, [projectId])

  useEffect(() => {
    void load()
  }, [load])

  const toggleNode = useCallback((nodeId: string) => {
    setExpandedIds((prev) => {
      const next = new Set(prev)
      if (next.has(nodeId)) next.delete(nodeId)
      else next.add(nodeId)
      return next
    })
  }, [])

  const expandAll = useCallback(() => {
    setExpandedIds(new Set(collectAllNodeIds(rootNodes)))
  }, [rootNodes])

  const collapseAll = useCallback(() => {
    setExpandedIds(new Set())
  }, [])

  const rows = useMemo(() => flattenWbsTree(rootNodes, expandedIds), [rootNodes, expandedIds])

  return {
    rootNodes,
    rows,
    loadState,
    loadError,
    reload: load,
    expandedIds,
    toggleNode,
    expandAll,
    collapseAll,
  }
}
