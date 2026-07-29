import { Navigate, Outlet, useLocation } from 'react-router-dom'
import { useAuthStore } from '../store/authStore'

/**
 * S4-FE-01 route guard: redirects to `/login` when there is no session at all, preserving the
 * originally-requested path as `?from=` so `LoginPage` can (in a later sprint) send the user back
 * where they started. Distinct from `RequireRole` (S2-FE-02), which handles the *authenticated but
 * wrong role* 403 case — this handles the *not authenticated at all* case, one layer up in the
 * route tree (wraps every `/app/:projectId/*` route, per that component's own doc comment
 * anticipating this).
 */
export function RequireAuth() {
  const isAuthenticated = useAuthStore((state) => state.isAuthenticated)
  const location = useLocation()

  if (!isAuthenticated) {
    return <Navigate to={`/login?from=${encodeURIComponent(location.pathname)}`} replace />
  }

  return <Outlet />
}
