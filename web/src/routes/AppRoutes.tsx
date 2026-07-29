import { Navigate, Route, Routes, useNavigate } from 'react-router-dom'
import { AppShell, NAV_ENTRIES } from '../components/layout'
import { LoginPage } from '../features/auth'
import { ProjectInfoPage } from '../features/info'
import { WbsPage } from '../features/wbs'
import { useAuthStore } from '../store/authStore'
import { useProjectStore } from '../store/projectStore'
import { RequireAuth } from './RequireAuth'
import { ScreenPlaceholder } from './ScreenPlaceholder'
import { SelectProjectPage } from './SelectProjectPage'

/** Real content for the two screens built this sprint (S4-FE-02/03); every other nav entry is a
 * `ScreenPlaceholder` until its own sprint lands (S4-FE-01 DoD). */
function screenElement(id: (typeof NAV_ENTRIES)[number]['id'], label: string) {
  if (id === 'info') return <ProjectInfoPage />
  if (id === 'wbs') return <WbsPage />
  return <ScreenPlaceholder title={label} />
}

function LoginRoute() {
  const navigate = useNavigate()
  return <LoginPage onLoginSuccess={() => navigate('/', { replace: true })} />
}

/** `/` has no screen of its own — it only ever decides where an authenticated visitor lands next
 * (the remembered project, per `projectStore.ts`) or sends an unauthenticated one to `/login`. */
function RootRedirect() {
  const isAuthenticated = useAuthStore((state) => state.isAuthenticated)
  const currentProjectId = useProjectStore((state) => state.currentProjectId)

  if (!isAuthenticated) return <Navigate to="/login" replace />
  if (!currentProjectId) return <Navigate to="/select-project" replace />
  return <Navigate to={`/app/${currentProjectId}/dashboard`} replace />
}

/**
 * S4-FE-01 top-level router (US-4.2): `/login` (public) -> `/select-project` (interim project
 * gate, see `store/projectStore.ts`) -> `/app/:projectId/*` (the shell + all 13 screens, per
 * `components/layout/navConfig.ts`). Every `/app/:projectId/*` route sits behind `RequireAuth`;
 * per-screen role gates (`RequireRole`, S2-FE-02) are applied inside each screen around its
 * specific mutation affordance (e.g. the Project Info edit button, the WBS batch-progress toggle)
 * rather than at the route level, since read access to both real screens this sprint is open to
 * any authenticated role — only the write action is role-restricted (mirrors the backend's own
 * `[Authorize(Roles = ...)]` placement on `ProjectsController`/`ProgressController`).
 */
export function AppRoutes() {
  return (
    <Routes>
      <Route path="/login" element={<LoginRoute />} />

      <Route element={<RequireAuth />}>
        <Route path="/select-project" element={<SelectProjectPage />} />

        <Route path="/app/:projectId" element={<AppShell />}>
          <Route index element={<Navigate to="dashboard" replace />} />
          {NAV_ENTRIES.map((entry) => (
            <Route key={entry.id} path={entry.path} element={screenElement(entry.id, entry.label)} />
          ))}
        </Route>
      </Route>

      <Route path="/" element={<RootRedirect />} />
      <Route path="*" element={<RootRedirect />} />
    </Routes>
  )
}
