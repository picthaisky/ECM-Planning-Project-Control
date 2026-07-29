import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { LoginPage } from './LoginPage'
import { useAuthStore } from '../../store/authStore'
import * as api from './api'

vi.mock('./api', async () => {
  const actual = await vi.importActual<typeof import('./api')>('./api')
  return { ...actual, login: vi.fn() }
})

const sampleResponse = {
  accessToken: 'jwt-abc',
  expiresAt: '2026-01-01T00:00:00+07:00',
  userId: 'user-1',
  tenantId: 'tenant-1',
  role: 'Admin' as const,
}

describe('LoginPage', () => {
  beforeEach(() => {
    useAuthStore.getState().logout()
    vi.mocked(api.login).mockReset()
  })

  it('renders the email/password form with a submit button using the Button primitive', () => {
    render(<LoginPage />)

    expect(screen.getByLabelText('อีเมล')).toBeInTheDocument()
    expect(screen.getByLabelText('รหัสผ่าน')).toBeInTheDocument()
    const submit = screen.getByRole('button', { name: 'เข้าสู่ระบบ' })
    expect(submit).toHaveAttribute('type', 'submit')
  })

  it('posts to the real login endpoint via login(), stores the session, and notifies onLoginSuccess', async () => {
    vi.mocked(api.login).mockResolvedValueOnce(sampleResponse)
    const onLoginSuccess = vi.fn()
    const user = userEvent.setup()

    render(<LoginPage onLoginSuccess={onLoginSuccess} />)
    await user.type(screen.getByLabelText('อีเมล'), 'admin@siam-construction.dev')
    await user.type(screen.getByLabelText('รหัสผ่าน'), 'Dev@CMPlus2026!')
    await user.click(screen.getByRole('button', { name: 'เข้าสู่ระบบ' }))

    await waitFor(() => expect(screen.getByText('เข้าสู่ระบบสำเร็จ')).toBeInTheDocument())

    expect(api.login).toHaveBeenCalledWith({
      email: 'admin@siam-construction.dev',
      password: 'Dev@CMPlus2026!',
    })
    expect(useAuthStore.getState().claims).toEqual({
      userId: 'user-1',
      tenantId: 'tenant-1',
      role: 'Admin',
    })
    expect(onLoginSuccess).toHaveBeenCalledTimes(1)
  })

  it('shows an inline Thai error and never populates the auth store on invalid credentials', async () => {
    vi.mocked(api.login).mockRejectedValueOnce(new api.LoginError('อีเมลหรือรหัสผ่านไม่ถูกต้อง', 401))
    const user = userEvent.setup()

    render(<LoginPage />)
    await user.type(screen.getByLabelText('อีเมล'), 'admin@siam-construction.dev')
    await user.type(screen.getByLabelText('รหัสผ่าน'), 'wrong-password')
    await user.click(screen.getByRole('button', { name: 'เข้าสู่ระบบ' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('อีเมลหรือรหัสผ่านไม่ถูกต้อง')
    expect(useAuthStore.getState().isAuthenticated).toBe(false)
    expect(screen.queryByText('เข้าสู่ระบบสำเร็จ')).not.toBeInTheDocument()
  })

  it('shows a logout button when already authenticated, and logout() clears the store', async () => {
    useAuthStore.getState().login(sampleResponse)
    const user = userEvent.setup()

    render(<LoginPage />)
    expect(screen.getByText('เข้าสู่ระบบสำเร็จ')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'ออกจากระบบ' }))

    expect(useAuthStore.getState().isAuthenticated).toBe(false)
  })
})
