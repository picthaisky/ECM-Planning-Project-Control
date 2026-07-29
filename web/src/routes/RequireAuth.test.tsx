import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it } from 'vitest'
import { RequireAuth } from './RequireAuth'
import { useAuthStore } from '../store/authStore'

describe('RequireAuth', () => {
  beforeEach(() => {
    useAuthStore.getState().logout()
  })

  it('redirects an unauthenticated visitor to /login', () => {
    render(
      <MemoryRouter initialEntries={['/app/project-1/wbs']}>
        <Routes>
          <Route path="/login" element={<div>login screen</div>} />
          <Route element={<RequireAuth />}>
            <Route path="/app/:projectId/wbs" element={<div>wbs screen</div>} />
          </Route>
        </Routes>
      </MemoryRouter>,
    )

    expect(screen.getByText('login screen')).toBeInTheDocument()
  })

  it('renders the protected route for an authenticated visitor', () => {
    useAuthStore.getState().login({
      accessToken: 'jwt',
      expiresAt: '2027-01-01T00:00:00+07:00',
      userId: 'user-1',
      tenantId: 'tenant-1',
      role: 'PM',
    })

    render(
      <MemoryRouter initialEntries={['/app/project-1/wbs']}>
        <Routes>
          <Route path="/login" element={<div>login screen</div>} />
          <Route element={<RequireAuth />}>
            <Route path="/app/:projectId/wbs" element={<div>wbs screen</div>} />
          </Route>
        </Routes>
      </MemoryRouter>,
    )

    expect(screen.getByText('wbs screen')).toBeInTheDocument()
  })
})
