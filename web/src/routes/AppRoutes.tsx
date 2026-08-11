import { Navigate, Route, Routes, useNavigate } from 'react-router-dom'
import { AppShell, NAV_ENTRIES } from '../components/layout'
import { LoginPage } from '../features/auth'
import { BaselinePage } from '../features/baseline'
import { CashFlowPage } from '../features/cash'
import { DashboardPage } from '../features/dashboard'
import { EvmPage } from '../features/evm'
import { GanttPage } from '../features/gantt'
import { ProjectInfoPage } from '../features/info'
import { IssuePage } from '../features/issue'
import { ManeqPage } from '../features/maneq'
import { PaymentPage } from '../features/payment'
import { PhotoPage } from '../features/photo'
import { TenantAdminPage } from '../features/tenant-admin'
import { VoPage } from '../features/vo'
import { WbsPage } from '../features/wbs'
import { WeatherPage } from '../features/weather'
import { useAuthStore } from '../store/authStore'
import { useProjectStore } from '../store/projectStore'
import { RequireAuth } from './RequireAuth'
import { RequireRole } from './RequireRole'
import { ScreenPlaceholder } from './ScreenPlaceholder'
import { SelectProjectPage } from './SelectProjectPage'

/** Real content for the screens built so far (S4-FE-02/03, S6-FE-01/02/03, S7-FE-01..04,
 * S8-FE-01/02, S9-FE-01/02, S10-FE-01/02, S11-FE-01, S12-FE-01/02, S14-FE-01/02); every other nav
 * entry is a `ScreenPlaceholder` until its own sprint lands (S4-FE-01 DoD) — see
 * `components/layout/navConfig.ts#IMPLEMENTED_SCREENS`, kept in sync with this function. */
function screenElement(id: (typeof NAV_ENTRIES)[number]['id'], label: string) {
  if (id === 'dashboard') return <DashboardPage />
  if (id === 'info') return <ProjectInfoPage />
  if (id === 'wbs') return <WbsPage />
  if (id === 'gantt') return <GanttPage />
  if (id === 'evm') return <EvmPage />
  if (id === 'cash') return <CashFlowPage />
  if (id === 'payment') return <PaymentPage />
  if (id === 'photo') return <PhotoPage />
  if (id === 'vo') return <VoPage />
  if (id === 'weather') return <WeatherPage />
  if (id === 'maneq') return <ManeqPage />
  if (id === 'issue') return <IssuePage />
  if (id === 'baseline') return <BaselinePage />
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
 *
 * `/tenant-admin` (S9-FE-03) is deliberately a **sibling** of `/app/:projectId`, not nested under
 * it — it configures a tenant-wide policy, never a single project's data (design.md §4: "a
 * tenant-level entry point outside the 13-screen project nav"), so it renders its own header rather
 * than `AppShell`/`Sidebar` (see `TenantAdminPage.tsx`'s own remarks) and is the first route in this
 * app gated by `RequireRole` at the *route* level rather than around one button — exactly the usage
 * `RequireRole`'s own doc comment anticipated ("the first one is Sprint 9's tenant-admin
 * approval-matrix UI"). A non-Admin who navigates here directly (bookmark, typed URL) sees the real
 * `ForbiddenPage` (403), never a blank page or a silent redirect; the backend enforces the actual
 * boundary independently regardless.
 */
export function AppRoutes() {
  return (
    <Routes>
      <Route path="/login" element={<LoginRoute />} />

      <Route element={<RequireAuth />}>
        <Route path="/select-project" element={<SelectProjectPage />} />

        <Route
          path="/tenant-admin"
          element={
            <RequireRole allowedRoles={['Admin']}>
              <TenantAdminPage />
            </RequireRole>
          }
        />

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
