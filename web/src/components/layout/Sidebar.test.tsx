import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it } from 'vitest'
import { Sidebar } from './Sidebar'
import { NAV_ENTRIES } from './navConfig'
import { useAuthStore } from '../../store/authStore'

function renderAt(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/app/:projectId/*" element={<Sidebar />} />
      </Routes>
    </MemoryRouter>,
  )
}

describe('Sidebar (S4-FE-01, ADR-0006 visual parity)', () => {
  beforeEach(() => {
    useAuthStore.getState().logout()
  })

  it('renders the exact 13-screen nav, in the prototype-transcribed order', () => {
    renderAt('/app/project-1/dashboard')

    const links = screen.getAllByRole('link')
    expect(links).toHaveLength(13)
    expect(links.map((l) => l.textContent)).toEqual(NAV_ENTRIES.map((e) => e.label))
  })

  it('the current screen gets the gold active-state background; every other item does not', () => {
    renderAt('/app/project-1/wbs')

    const wbsLink = screen.getByRole('link', { name: 'WBS & Activity' })
    const infoLink = screen.getByRole('link', { name: 'ข้อมูลโครงการ' })

    expect(wbsLink.className).toContain('bg-gold')
    expect(wbsLink.className).toContain('text-navy')
    expect(infoLink.className).not.toContain('bg-gold')
    expect(infoLink.className).toContain('text-white/75')
  })

  it('links route under /app/:projectId/<screen>, preserving the current project id', () => {
    renderAt('/app/project-42/wbs')

    expect(screen.getByRole('link', { name: 'Payment Certificate' })).toHaveAttribute(
      'href',
      '/app/project-42/payment',
    )
  })

  it('shows the logged-in user role and logs out on click', async () => {
    useAuthStore.getState().login({
      accessToken: 'jwt',
      expiresAt: '2027-01-01T00:00:00+07:00',
      userId: 'user-1',
      tenantId: 'tenant-1',
      role: 'PM',
    })

    renderAt('/app/project-1/dashboard')
    expect(screen.getByText('Project Manager')).toBeInTheDocument()

    screen.getByRole('button', { name: 'ออกจากระบบ' }).click()
    expect(useAuthStore.getState().isAuthenticated).toBe(false)
  })
})
