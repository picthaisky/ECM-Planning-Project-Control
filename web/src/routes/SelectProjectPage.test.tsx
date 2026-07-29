import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it } from 'vitest'
import { SelectProjectPage } from './SelectProjectPage'
import { useProjectStore } from '../store/projectStore'

describe('SelectProjectPage', () => {
  beforeEach(() => {
    useProjectStore.getState().clearCurrentProjectId()
  })

  it('rejects a non-GUID value with a Thai error and never updates the store', async () => {
    const user = userEvent.setup()
    render(
      <MemoryRouter>
        <SelectProjectPage />
      </MemoryRouter>,
    )

    await user.type(screen.getByLabelText('รหัสโครงการ (Project ID)'), 'not-a-guid')
    await user.click(screen.getByRole('button', { name: 'เข้าสู่โครงการ' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('รูปแบบ GUID')
    expect(useProjectStore.getState().currentProjectId).toBeNull()
  })

  it('accepts a valid GUID, stores it, and navigates into the shell', async () => {
    const user = userEvent.setup()
    render(
      <MemoryRouter initialEntries={['/select-project']}>
        <Routes>
          <Route path="/select-project" element={<SelectProjectPage />} />
          <Route path="/app/:projectId/dashboard" element={<div>dashboard screen</div>} />
        </Routes>
      </MemoryRouter>,
    )

    await user.type(
      screen.getByLabelText('รหัสโครงการ (Project ID)'),
      '3fa85f64-5717-4562-b3fc-2c963f66afa6',
    )
    await user.click(screen.getByRole('button', { name: 'เข้าสู่โครงการ' }))

    expect(useProjectStore.getState().currentProjectId).toBe('3fa85f64-5717-4562-b3fc-2c963f66afa6')
    expect(await screen.findByText('dashboard screen')).toBeInTheDocument()
  })
})
