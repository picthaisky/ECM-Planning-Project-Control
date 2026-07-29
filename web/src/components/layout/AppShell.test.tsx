import { render, screen, within } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { describe, expect, it } from 'vitest'
import { AppShell } from './AppShell'
import { useProjectStore } from '../../store/projectStore'

describe('AppShell (S4-FE-01)', () => {
  it('renders the sidebar, topbar (with the matched screen title), and the routed child screen', () => {
    render(
      <MemoryRouter initialEntries={['/app/project-1/wbs']}>
        <Routes>
          <Route path="/app/:projectId" element={<AppShell />}>
            <Route path="wbs" element={<div>WBS screen content</div>} />
          </Route>
        </Routes>
      </MemoryRouter>,
    )

    expect(screen.getByRole('navigation', { name: 'เมนูหลัก' })).toBeInTheDocument()
    // "WBS & Activity" legitimately appears twice (the active Sidebar nav item + the Topbar
    // title derived from the same path) — scope to the Topbar's <header> to assert the latter.
    expect(within(screen.getByRole('banner')).getByText('WBS & Activity')).toBeInTheDocument()
    expect(screen.getByText('WBS screen content')).toBeInTheDocument()
  })

  it('remembers the routed :projectId in projectStore for the next bare "/" visit', () => {
    useProjectStore.getState().clearCurrentProjectId()

    render(
      <MemoryRouter initialEntries={['/app/project-99/info']}>
        <Routes>
          <Route path="/app/:projectId" element={<AppShell />}>
            <Route path="info" element={<div>info</div>} />
          </Route>
        </Routes>
      </MemoryRouter>,
    )

    expect(useProjectStore.getState().currentProjectId).toBe('project-99')
  })
})
