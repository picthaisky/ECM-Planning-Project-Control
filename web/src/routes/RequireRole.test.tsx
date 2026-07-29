import { render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it } from 'vitest'
import { RequireRole } from './RequireRole'
import { useAuthStore } from '../store/authStore'

const FIXTURE_TEXT = 'เนื้อหาสำหรับผู้ดูแลระบบเท่านั้น'

/** Stand-in for a real Admin-only route element - none exists yet this sprint (S2-FE-02 DoD). */
function AdminOnlyFixture() {
  return <div>{FIXTURE_TEXT}</div>
}

function loginAs(role: 'Admin' | 'Site') {
  useAuthStore.getState().login({
    accessToken: 'jwt-abc',
    expiresAt: '2026-01-01T00:00:00+07:00',
    userId: 'u1',
    tenantId: 't1',
    role,
  })
}

describe('RequireRole', () => {
  beforeEach(() => {
    useAuthStore.getState().logout()
  })

  it('renders the (default null) fallback when there is no session at all', () => {
    const { container } = render(
      <RequireRole allowedRoles={['Admin']}>
        <AdminOnlyFixture />
      </RequireRole>,
    )
    expect(container).toBeEmptyDOMElement()
  })

  it('DoD: navigating to an Admin-only fixture as a non-Admin role shows a real 403 state', () => {
    loginAs('Site')

    render(
      <RequireRole allowedRoles={['Admin']}>
        <AdminOnlyFixture />
      </RequireRole>,
    )

    expect(screen.getByRole('alert')).toHaveTextContent('403')
    expect(screen.queryByText(FIXTURE_TEXT)).not.toBeInTheDocument()
  })

  it('renders children when the authenticated role is in allowedRoles', () => {
    loginAs('Admin')

    render(
      <RequireRole allowedRoles={['Admin']}>
        <AdminOnlyFixture />
      </RequireRole>,
    )

    expect(screen.getByText(FIXTURE_TEXT)).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('accepts multiple allowed roles', () => {
    loginAs('Site')

    render(
      <RequireRole allowedRoles={['Admin', 'Site']}>
        <AdminOnlyFixture />
      </RequireRole>,
    )

    expect(screen.getByText(FIXTURE_TEXT)).toBeInTheDocument()
  })
})
