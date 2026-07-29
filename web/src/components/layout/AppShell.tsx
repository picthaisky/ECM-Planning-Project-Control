import { useEffect } from 'react'
import { Outlet, useLocation, useParams } from 'react-router-dom'
import { useProjectStore } from '../../store/projectStore'
import { Sidebar } from './Sidebar'
import { Topbar } from './Topbar'
import { NAV_ENTRIES } from './navConfig'

function currentPageTitle(pathname: string): string {
  const lastSegment = pathname.split('/').filter(Boolean).pop()
  return NAV_ENTRIES.find((entry) => entry.path === lastSegment)?.label ?? 'CM+ Project Control'
}

/**
 * The app shell (S4-FE-01, US-4.2): navy `Sidebar` + `Topbar` + a scrollable content area that
 * renders whichever of the 13 screens the router matched (`<Outlet />`). Mounted once at
 * `/app/:projectId` (see `routes/AppRoutes.tsx`) so every screen — built or still a
 * `ScreenPlaceholder` — shares identical chrome, matching the prototype's
 * `display:flex;height:100vh` shell exactly (ADR-0006).
 */
export function AppShell() {
  const location = useLocation()
  const { projectId } = useParams<{ projectId: string }>()
  const setCurrentProjectId = useProjectStore((state) => state.setCurrentProjectId)

  // Whichever project the URL names is authoritative; remember it (projectStore.ts) so a bare
  // `/` visit next time lands back here instead of the select-project gate.
  useEffect(() => {
    if (projectId) setCurrentProjectId(projectId)
  }, [projectId, setCurrentProjectId])

  return (
    <div className="flex h-screen min-h-[720px] overflow-hidden bg-bg">
      <Sidebar />
      <div className="flex min-w-0 flex-1 flex-col">
        <Topbar pageTitle={currentPageTitle(location.pathname)} />
        <main className="flex-1 overflow-auto px-[22px] py-[18px]">
          <Outlet />
        </main>
      </div>
    </div>
  )
}
