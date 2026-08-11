import { useMemo, useState } from 'react'
import { useParams } from 'react-router-dom'
import { Button } from '../../components'
import { useAuthStore } from '../../store/authStore'
import type { UserRole } from '../../store/authStore'
import { IssueCreateModal } from './components/IssueCreateModal'
import { IssueSummaryTiles } from './components/IssueSummaryTiles'
import { IssueTable } from './components/IssueTable'
import { ISSUE_STATUS_LABELS, searchIssues } from './issueLabels'
import { useIssueActions } from './useIssueActions'
import { useIssues } from './useIssues'
import type { IssueStatus } from './types'

/** Mirrors `ProjectIssuesController.WriteRoles` exactly (PM/Planning/Site/QS/Admin) — shared by both
 * "แจ้งปัญหาใหม่" and the per-row advance-status action. UX gate only; the server re-checks. */
const ISSUE_WRITE_ROLES: readonly UserRole[] = ['PM', 'Planning', 'Site', 'QS', 'Admin']

type StatusFilter = 'All' | IssueStatus

const STATUS_FILTERS: StatusFilter[] = ['All', 'Open', 'Doing', 'Closed']

/**
 * S11-FE-01 "Issue / Action Log" screen (US-11.2): the four real summary tiles (sourced only from
 * `IssueListResultDto.statusCounts`/`totalCount` — see `IssueSummaryTiles.tsx`'s own remarks) plus
 * the issue register with the inline advance-status action.
 *
 * **Search/status-filter here are client-side only, over the already-fully-loaded `result.items`.**
 * `ListIssuesQueryHandler` does not implement server-side pagination or filtering yet (`types.ts`'s
 * own remarks, verified from source) — this screen does not pretend otherwise: there is no "page 1
 * of N" control anywhere, because every row is already in hand and a client-side filter is honest
 * about that. If the project's issue count grows large enough that shipping the whole list on every
 * load becomes a real cost, that is a backend scope addition (flagged in this feature's frontend
 * report), not something to fake here.
 */
export function IssuePage() {
  const { projectId } = useParams<{ projectId: string }>()
  const currentUserRole = useAuthStore((state) => state.claims?.role ?? null)

  const issues = useIssues(projectId ?? '')
  const actions = useIssueActions(projectId ?? '', () => void issues.reload())

  const [createModalOpen, setCreateModalOpen] = useState(false)
  const [statusFilter, setStatusFilter] = useState<StatusFilter>('All')
  const [search, setSearch] = useState('')

  const visibleItems = useMemo(() => {
    const byStatus = statusFilter === 'All' ? issues.result.items : issues.result.items.filter((i) => i.status === statusFilter)
    return searchIssues(byStatus, search)
  }, [issues.result.items, statusFilter, search])

  if (!projectId) return null

  const canWrite = currentUserRole !== null && ISSUE_WRITE_ROLES.includes(currentUserRole)

  return (
    <div className="flex flex-col gap-4">
      <IssueSummaryTiles
        totalCount={issues.result.totalCount}
        statusCounts={issues.result.statusCounts}
        state={issues.loadState}
        errorMessage={issues.loadError ?? undefined}
      />

      <div className="overflow-hidden rounded-card border border-border bg-surface">
        <div className="flex flex-wrap items-center gap-2 px-4 py-3">
          <div className="font-heading text-[13.5px] font-semibold text-navy">
            ปัญหาหน้างาน / Action Log — กดปุ่มเพื่อเลื่อนสถานะ
          </div>

          {canWrite && (
            <Button size="sm" className="ml-auto" onClick={() => setCreateModalOpen(true)}>
              + แจ้งปัญหาใหม่
            </Button>
          )}
        </div>

        <div className="flex flex-wrap items-center gap-2 border-t border-border-subtle px-4 py-2.5">
          <div className="flex gap-1">
            {STATUS_FILTERS.map((filter) => (
              <button
                key={filter}
                type="button"
                onClick={() => setStatusFilter(filter)}
                className={`rounded px-2.5 py-1 text-[11px] font-medium transition-colors ${
                  statusFilter === filter ? 'bg-navy text-white' : 'text-text-muted hover:bg-bg'
                }`}
              >
                {filter === 'All' ? 'ทั้งหมด' : ISSUE_STATUS_LABELS[filter]}
              </button>
            ))}
          </div>
          <input
            type="search"
            placeholder="ค้นหาหัวข้อ / รายละเอียด / ผู้รับผิดชอบ"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            className="ml-auto w-64 rounded-card border border-border px-3 py-1.5 text-xs text-text focus:border-navy focus:outline-none"
          />
        </div>

        <IssueTable
          items={visibleItems}
          state={issues.loadState}
          errorMessage={issues.loadError ?? undefined}
          canWrite={canWrite}
          advancingId={actions.advancingId}
          onAdvance={(issue) => void actions.advance(issue.id)}
        />

        {actions.advanceError && (
          <p role="alert" className="border-t border-border-subtle px-4 py-2 text-xs text-danger">
            {actions.advanceError}
          </p>
        )}
      </div>

      <IssueCreateModal
        isOpen={createModalOpen}
        onClose={() => {
          actions.clearCreateError()
          setCreateModalOpen(false)
        }}
        busy={actions.creating}
        errorMessage={actions.createError}
        onSubmit={(payload) => {
          void actions.create(payload).then((saved) => {
            if (saved) setCreateModalOpen(false)
          })
        }}
      />
    </div>
  )
}
