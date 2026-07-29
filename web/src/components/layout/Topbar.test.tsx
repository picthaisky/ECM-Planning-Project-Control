import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { describe, expect, it } from 'vitest'
import { Topbar } from './Topbar'

describe('Topbar (S4-FE-01)', () => {
  it('renders the given page title and the routed project id, never a fabricated project name', () => {
    render(
      <MemoryRouter initialEntries={['/app/project-1']}>
        <Routes>
          <Route path="/app/:projectId" element={<Topbar pageTitle="WBS & Activity" />} />
        </Routes>
      </MemoryRouter>,
    )

    expect(screen.getByText('WBS & Activity')).toBeInTheDocument()
    expect(screen.getByText(/project-1/)).toBeInTheDocument()
  })

  it('quick-action buttons are disabled placeholders (out of this sprint scope), not fake success', () => {
    render(
      <MemoryRouter initialEntries={['/app/project-1']}>
        <Routes>
          <Route path="/app/:projectId" element={<Topbar pageTitle="Dashboard" />} />
        </Routes>
      </MemoryRouter>,
    )

    const excel = screen.getByText('Excel')
    expect(excel).toHaveAttribute('aria-disabled', 'true')
    expect(excel).toHaveAttribute('title', 'พร้อมใช้งานในสปรินต์ถัดไป')
  })
})
