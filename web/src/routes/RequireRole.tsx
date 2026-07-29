import type { ReactNode } from 'react'
import { useAuthStore } from '../store/authStore'
import type { UserRole } from '../store/authStore'
import { ForbiddenPage } from './ForbiddenPage'

export interface RequireRoleProps {
  /** Role(s) allowed to see `children`. An authenticated user whose role is not in this list sees
   * `ForbiddenPage` instead. */
  allowedRoles: UserRole[]
  children: ReactNode
  /** Rendered instead of children/ForbiddenPage when there is no session at all. Defaults to
   * `null` — Sprint 4's app shell (S4-FE-01) is expected to gate an entire authenticated section
   * behind its own login check before any `RequireRole`-wrapped route is ever reached, so an
   * unauthenticated visitor should not normally hit this component in the first place. */
  unauthenticatedFallback?: ReactNode
}

/**
 * S2-FE-02 role-based route guard (US-2.1). Framework-agnostic on purpose: it wraps `children`
 * rather than depending on a router library, so it drops directly into whichever routing solution
 * Sprint 4's app shell adopts (e.g. as a route `element`) without this sprint needing to pull one
 * in and pre-empt that decision.
 *
 * No real Admin-only route exists yet (Sprint 9's tenant-admin approval-matrix UI is the first
 * one) — proven here against a fixture component instead (see `RequireRole.test.tsx`), since
 * there is nothing real to wire it to this sprint.
 *
 * This is a UX gate only, never the authorization boundary: every mutating/reading endpoint is
 * still authorized server-side from the JWT's own role + tenant claims (S2-BE-01) — a user could
 * always disable JavaScript or hand-craft a request, and the API must reject it independently of
 * this component.
 */
export function RequireRole({ allowedRoles, children, unauthenticatedFallback = null }: RequireRoleProps) {
  const claims = useAuthStore((state) => state.claims)

  if (!claims) {
    return <>{unauthenticatedFallback}</>
  }

  if (!allowedRoles.includes(claims.role)) {
    return <ForbiddenPage />
  }

  return <>{children}</>
}
