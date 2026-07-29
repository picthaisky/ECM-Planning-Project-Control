import { useMemo, useState } from 'react'
import { useParams } from 'react-router-dom'
import { Button } from '../../components'
import { RequireRole } from '../../routes'
import { searchWbsTree } from './flattenTree'
import { ProgressUpdatePanel } from './ProgressUpdatePanel'
import { useBatchProgressForm } from './useBatchProgressForm'
import { useNodeActivities } from './useNodeActivities'
import { useWbsTree } from './useWbsTree'
import { WbsTreeGrid } from './WbsTreeGrid'

/** Roles allowed to submit a batch progress update — mirrors `ProgressController`'s
 * `[Authorize(Roles = "PM,Planning,Site,QS,Admin")]` exactly (viewing the WBS tree itself has no
 * role restriction on the backend, so only the "โหมดอัปเดตความคืบหน้า" toggle is gated). */
const PROGRESS_UPDATE_ROLES = ['PM', 'Planning', 'Site', 'QS', 'Admin'] as const

/**
 * S4-FE-03 "WBS & Activity" screen (US-4.1, US-4.5): the real, virtualized WBS tree
 * (`GET .../wbs-tree`) plus the batch "โหมดอัปเดตความคืบหน้า" grid calling the real
 * `POST .../progress/batch`. Toggle gated by `RequireRole` since only certain roles may submit a
 * batch (view access is open to any authenticated role, matching the backend).
 */
export function WbsPage() {
  const { projectId } = useParams<{ projectId: string }>()
  const tree = useWbsTree(projectId ?? '')
  const nodeActivities = useNodeActivities()
  const batchForm = useBatchProgressForm(projectId ?? '')
  const [mode, setMode] = useState<'view' | 'update-progress'>('view')
  const [search, setSearch] = useState('')

  const displayRows = useMemo(
    () => (search.trim() ? searchWbsTree(tree.rootNodes, search) : tree.rows),
    [search, tree.rootNodes, tree.rows],
  )

  if (!projectId) return null

  async function handlePickForProgress(nodeId: string) {
    const activities = await nodeActivities.loadForNode(projectId!, nodeId)
    if (activities.length > 0) batchForm.addActivities(activities)
  }

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center gap-2">
        <input
          type="search"
          placeholder="ค้นหาตาม WBS Code / ชื่องาน (branch / zone)"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          className="w-72 rounded-card border border-border px-3 py-1.5 text-xs text-text focus:border-navy focus:outline-none"
        />
        <Button size="sm" variant="secondary" onClick={tree.expandAll} disabled={!!search.trim()}>
          ขยายทั้งหมด
        </Button>
        <Button size="sm" variant="secondary" onClick={tree.collapseAll} disabled={!!search.trim()}>
          ย่อทั้งหมด
        </Button>

        <RequireRole allowedRoles={[...PROGRESS_UPDATE_ROLES]}>
          <Button
            size="sm"
            variant={mode === 'update-progress' ? 'primary' : 'secondary'}
            className="ml-auto"
            onClick={() => setMode(mode === 'update-progress' ? 'view' : 'update-progress')}
          >
            {mode === 'update-progress' ? 'ปิดโหมดอัปเดตความคืบหน้า' : 'โหมดอัปเดตความคืบหน้า'}
          </Button>
        </RequireRole>
      </div>

      <WbsTreeGrid
        rows={displayRows}
        state={tree.loadState === 'loading' ? 'loading' : tree.loadState === 'error' ? 'error' : 'ready'}
        errorMessage={tree.loadError ?? undefined}
        onToggle={tree.toggleNode}
        onPickForProgress={
          mode === 'update-progress' ? (row) => void handlePickForProgress(row.node.id) : undefined
        }
      />

      {mode === 'update-progress' && (
        <ProgressUpdatePanel
          form={batchForm}
          nodeActivitiesLoading={nodeActivities.state === 'loading'}
          nodeActivitiesError={nodeActivities.state === 'error' ? nodeActivities.error : null}
        />
      )}
    </div>
  )
}
