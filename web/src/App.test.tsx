import { render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import App from './App'
import { useAuthStore } from './store/authStore'
import { useProjectStore } from './store/projectStore'

describe('App (S4-FE-01 shell root)', () => {
  beforeEach(() => {
    useAuthStore.getState().logout()
    useProjectStore.getState().clearCurrentProjectId()
    window.history.pushState({}, '', '/')
  })

  afterEach(() => {
    window.history.pushState({}, '', '/')
  })

  it('an unauthenticated visitor is redirected to the login screen', () => {
    render(<App />)
    expect(screen.getByRole('button', { name: 'เข้าสู่ระบบ' })).toBeInTheDocument()
  })

  it('an authenticated visitor with no remembered project is sent to the project-selection gate', () => {
    useAuthStore.getState().login({
      accessToken: 'jwt',
      expiresAt: '2027-01-01T00:00:00+07:00',
      userId: 'user-1',
      tenantId: 'tenant-1',
      role: 'PM',
    })

    render(<App />)
    expect(screen.getByRole('heading', { name: 'เลือกโครงการ' })).toBeInTheDocument()
  })

  it('an authenticated visitor with a remembered project lands on the app shell dashboard', () => {
    useAuthStore.getState().login({
      accessToken: 'jwt',
      expiresAt: '2027-01-01T00:00:00+07:00',
      userId: 'user-1',
      tenantId: 'tenant-1',
      role: 'PM',
    })
    useProjectStore.getState().setCurrentProjectId('3fa85f64-5717-4562-b3fc-2c963f66afa6')

    render(<App />)
    expect(screen.getByRole('navigation', { name: 'เมนูหลัก' })).toBeInTheDocument()
    expect(screen.getByText('ข้อมูลโครงการ')).toBeInTheDocument()
  })
})
