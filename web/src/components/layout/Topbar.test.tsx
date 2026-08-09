import { beforeEach, describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { Topbar } from './Topbar'
import { useAuthStore } from '../../store/authStore'

function renderTopbar(pageTitle: string) {
  return render(
    <MemoryRouter initialEntries={['/app/project-1']}>
      <Routes>
        <Route path="/app/:projectId" element={<Topbar pageTitle={pageTitle} />} />
      </Routes>
    </MemoryRouter>,
  )
}

describe('Topbar (S4-FE-01)', () => {
  beforeEach(() => {
    useAuthStore.getState().logout()
  })

  it('renders the given page title and the routed project id, never a fabricated project name', () => {
    renderTopbar('WBS & Activity')

    expect(screen.getByText('WBS & Activity')).toBeInTheDocument()
    expect(screen.getByText(/project-1/)).toBeInTheDocument()
  })

  it('quick-action buttons are disabled placeholders (out of this sprint scope), not fake success', () => {
    renderTopbar('Dashboard')

    const excel = screen.getByText('Excel')
    expect(excel).toHaveAttribute('aria-disabled', 'true')
    expect(excel).toHaveAttribute('title', 'พร้อมใช้งานในสปรินต์ถัดไป')
  })

  describe('S9-FE-03 Tenant Admin entry point', () => {
    it('shows a real, working "Tenant Admin" link for an Admin — not a disabled placeholder', () => {
      useAuthStore.getState().login({
        accessToken: 'jwt',
        expiresAt: '2027-01-01T00:00:00+07:00',
        userId: 'admin-1',
        tenantId: 'tenant-1',
        role: 'Admin',
      })
      renderTopbar('Dashboard')

      const link = screen.getByRole('link', { name: '⚙ Tenant Admin' })
      expect(link).toHaveAttribute('href', '/tenant-admin')
      expect(link).not.toHaveAttribute('aria-disabled')
    })

    it('hides the link entirely for every non-Admin role', () => {
      useAuthStore.getState().login({
        accessToken: 'jwt',
        expiresAt: '2027-01-01T00:00:00+07:00',
        userId: 'pm-1',
        tenantId: 'tenant-1',
        role: 'PM',
      })
      renderTopbar('Dashboard')

      expect(screen.queryByRole('link', { name: '⚙ Tenant Admin' })).not.toBeInTheDocument()
    })

    it('hides the link when there is no session at all', () => {
      renderTopbar('Dashboard')
      expect(screen.queryByRole('link', { name: '⚙ Tenant Admin' })).not.toBeInTheDocument()
    })
  })
})
