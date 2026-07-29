import { useParams } from 'react-router-dom'
import { cx } from '../../utils/cx'

export interface TopbarProps {
  /** Current screen's label (from `navConfig.ts`), shown as the page title exactly like the
   * prototype's `{{ pageTitle }}` binding. */
  pageTitle: string
}

interface QuickAction {
  label: string
  toneClass: string
}

/**
 * The prototype's top-bar quick-action row (⟳ คำนวณ / Excel / MS Project / P6 XER / Export PDF)
 * is global chrome spanning several *later* sprints' features (CPM recalculation is Sprint 5,
 * exports are their own screens' concerns) — out of this sprint's scope (S4-FE-01 is shell +
 * nav only). Rendered here **disabled**, matching the prototype's spacing/colors for the ADR-0006
 * layout-proportion check, rather than wired to a fake success toast that would misrepresent
 * functionality that does not exist yet.
 */
const QUICK_ACTIONS: QuickAction[] = [
  { label: '⟳ คำนวณ', toneClass: 'text-navy' },
  { label: 'Excel', toneClass: 'text-success' },
  { label: 'MS Project', toneClass: 'text-secondary' },
  { label: 'P6 (.XER)', toneClass: 'text-danger' },
]

/**
 * Top bar (S4-FE-01, US-4.2): page title + project context line, matching the prototype's
 * `13px 22px` padding and white/border styling. The project meta line only ever shows what the
 * client actually knows (the `:projectId` route param) — there is no `GET` project-detail
 * endpoint yet (see `store/projectStore.ts`'s remarks), so it deliberately does not fabricate a
 * project name/data-date the way the prototype's static mock does.
 */
export function Topbar({ pageTitle }: TopbarProps) {
  const { projectId } = useParams<{ projectId: string }>()

  return (
    <header className="flex flex-none items-center gap-3.5 border-b border-border bg-surface px-[22px] py-[13px]">
      <div>
        <div className="font-heading text-base font-semibold text-navy">{pageTitle}</div>
        <div className="text-[11.5px] text-text-faint">โครงการ: {projectId}</div>
      </div>

      <div className="ml-auto flex items-center gap-2">
        {QUICK_ACTIONS.map((action) => (
          <span
            key={action.label}
            aria-disabled="true"
            title="พร้อมใช้งานในสปรินต์ถัดไป"
            className={cx(
              'cursor-not-allowed rounded-md border border-border px-3 py-[5px] text-[11.5px] font-semibold opacity-50',
              action.toneClass,
            )}
          >
            {action.label}
          </span>
        ))}
        <div className="h-5 w-px bg-border" aria-hidden="true" />
        <span
          aria-disabled="true"
          title="พร้อมใช้งานในสปรินต์ถัดไป"
          className="cursor-not-allowed rounded-md bg-navy px-3 py-[5px] text-[11.5px] font-medium text-white opacity-50"
        >
          Export PDF
        </span>
      </div>
    </header>
  )
}
