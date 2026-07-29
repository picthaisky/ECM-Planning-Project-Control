import { beforeEach, describe, expect, it } from 'vitest'
import { useAuthStore } from './authStore'

const sampleSession = {
  accessToken: 'jwt-abc',
  expiresAt: '2026-01-01T00:00:00+07:00',
  userId: 'u1',
  tenantId: 't1',
  role: 'PM' as const,
}

describe('authStore', () => {
  beforeEach(() => {
    useAuthStore.getState().logout()
  })

  it('starts unauthenticated with no token/claims', () => {
    const state = useAuthStore.getState()
    expect(state.isAuthenticated).toBe(false)
    expect(state.accessToken).toBeNull()
    expect(state.expiresAt).toBeNull()
    expect(state.claims).toBeNull()
  })

  it('login() stores the token, expiry, and exactly the three routing claims', () => {
    useAuthStore.getState().login(sampleSession)

    const state = useAuthStore.getState()
    expect(state.isAuthenticated).toBe(true)
    expect(state.accessToken).toBe('jwt-abc')
    expect(state.expiresAt).toBe('2026-01-01T00:00:00+07:00')
    expect(state.claims).toEqual({ userId: 'u1', tenantId: 't1', role: 'PM' })
  })

  it('logout() clears the session entirely', () => {
    useAuthStore.getState().login(sampleSession)
    useAuthStore.getState().logout()

    const state = useAuthStore.getState()
    expect(state.isAuthenticated).toBe(false)
    expect(state.accessToken).toBeNull()
    expect(state.expiresAt).toBeNull()
    expect(state.claims).toBeNull()
  })
})
