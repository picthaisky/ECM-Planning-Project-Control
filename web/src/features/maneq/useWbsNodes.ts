import { useEffect, useState } from 'react'
import { listWbsNodes } from './api'
import type { WbsNodeOptionDto } from './types'

/**
 * Loads the project's WBS nodes (flattened from `GET /projects/{id}/wbs-tree`) for the
 * record/correction form's optional WBS-node dropdown. Degrades to an empty list on any failure — the
 * form then falls back to a raw-GUID text input, so a tree-load outage never blocks logging.
 */
export function useWbsNodes(projectId: string): WbsNodeOptionDto[] {
  const [nodes, setNodes] = useState<WbsNodeOptionDto[]>([])

  useEffect(() => {
    if (!projectId) return
    let cancelled = false
    listWbsNodes(projectId)
      .then((loaded) => {
        if (!cancelled) setNodes(loaded)
      })
      .catch(() => {
        if (!cancelled) setNodes([])
      })
    return () => {
      cancelled = true
    }
  }, [projectId])

  return nodes
}
