import { useId, useState } from 'react'
import type { FormEvent } from 'react'
import { Button } from '../../components'
import { useAuthStore } from '../../store/authStore'
import { login, LoginError } from './api'

export interface LoginPageProps {
  /**
   * Called after a successful login, in addition to updating the auth store — e.g. Sprint 4's
   * app shell (S4-FE-01) can use this to navigate away. Optional: this screen is fully functional
   * on its own (it shows an authenticated placeholder state) even with no destination screen
   * wired up yet.
   */
  onLoginSuccess?: () => void
}

/**
 * S2-FE-01 login screen (US-2.1). Posts to the real `POST /api/v1/auth/login`; on success, stores
 * the session in `useAuthStore` (in-memory only — see `authStore.ts` for the storage rationale)
 * and calls `onLoginSuccess` if supplied.
 *
 * There is no post-login destination screen yet — the app shell is Sprint 4 (S4-FE-01) — so this
 * screen renders its own minimal authenticated placeholder, making it a complete, demoable
 * feature on its own rather than a dead end.
 */
export function LoginPage({ onLoginSuccess }: LoginPageProps) {
  const emailId = useId()
  const passwordId = useId()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [submitting, setSubmitting] = useState(false)
  const [errorMessage, setErrorMessage] = useState<string | null>(null)

  const claims = useAuthStore((state) => state.claims)
  const isAuthenticated = useAuthStore((state) => state.isAuthenticated)
  const storeLogin = useAuthStore((state) => state.login)
  const logout = useAuthStore((state) => state.logout)

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setErrorMessage(null)
    setSubmitting(true)

    try {
      const session = await login({ email, password })
      storeLogin(session)
      onLoginSuccess?.()
    } catch (error) {
      setErrorMessage(error instanceof LoginError ? error.message : 'เข้าสู่ระบบไม่สำเร็จ กรุณาลองใหม่อีกครั้ง')
    } finally {
      setSubmitting(false)
    }
  }

  if (isAuthenticated && claims) {
    return (
      <div className="flex min-h-screen items-center justify-center bg-bg px-4">
        <div className="w-full max-w-sm rounded-card border border-border bg-surface p-8 text-center">
          <h1 className="font-heading text-xl font-bold text-navy">เข้าสู่ระบบสำเร็จ</h1>
          <p className="mt-2 text-sm text-text-muted">
            บทบาท: <span className="font-medium text-navy">{claims.role}</span>
          </p>
          <p className="mt-1 text-xs text-text-faint">Tenant ID: {claims.tenantId}</p>
          <Button variant="secondary" className="mt-6" onClick={logout}>
            ออกจากระบบ
          </Button>
        </div>
      </div>
    )
  }

  return (
    <div className="flex min-h-screen items-center justify-center bg-bg px-4">
      <form
        onSubmit={handleSubmit}
        noValidate
        className="w-full max-w-sm rounded-card border border-border bg-surface p-8"
      >
        <h1 className="font-heading text-xl font-bold text-navy">CM+ Project Control</h1>
        <p className="mt-1 text-sm text-text-muted">เข้าสู่ระบบเพื่อดำเนินการต่อ</p>

        <label htmlFor={emailId} className="mt-6 block text-xs font-medium text-text-muted">
          อีเมล
        </label>
        <input
          id={emailId}
          type="email"
          autoComplete="username"
          required
          value={email}
          onChange={(event) => setEmail(event.target.value)}
          className="mt-1 w-full rounded-card border border-border px-3 py-2 text-sm text-text focus:border-navy focus:outline-none"
        />

        <label htmlFor={passwordId} className="mt-4 block text-xs font-medium text-text-muted">
          รหัสผ่าน
        </label>
        <input
          id={passwordId}
          type="password"
          autoComplete="current-password"
          required
          value={password}
          onChange={(event) => setPassword(event.target.value)}
          className="mt-1 w-full rounded-card border border-border px-3 py-2 text-sm text-text focus:border-navy focus:outline-none"
        />

        {errorMessage && (
          <p role="alert" className="mt-4 text-xs text-danger">
            {errorMessage}
          </p>
        )}

        <Button type="submit" loading={submitting} className="mt-6 w-full">
          เข้าสู่ระบบ
        </Button>
      </form>
    </div>
  )
}
