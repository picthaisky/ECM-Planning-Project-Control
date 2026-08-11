import { beforeEach, describe, expect, it, vi } from 'vitest'
import { registerOutboxLogoutQuarantine } from './authLifecycle'
import { quarantineOwnerBlobs } from './outboxMaintenance'
import { useAuthStore } from '../../store/authStore'
import type { AuthSession } from '../../store/authStore'

vi.mock('./outboxMaintenance', () => ({ quarantineOwnerBlobs: vi.fn().mockResolvedValue(0) }))

const SESSION_A: AuthSession = {
  accessToken: 'jwt-a',
  expiresAt: '2027-01-01T00:00:00+07:00',
  userId: 'user-a',
  tenantId: 'tenant-1',
  role: 'Site',
}
const SESSION_B: AuthSession = {
  accessToken: 'jwt-b',
  expiresAt: '2027-01-01T00:00:00+07:00',
  userId: 'user-b',
  tenantId: 'tenant-2',
  role: 'Site',
}

describe('registerOutboxLogoutQuarantine (H-02 fix bullet 2, Sprint 12 security review)', () => {
  beforeEach(() => {
    useAuthStore.getState().logout()
    vi.mocked(quarantineOwnerBlobs).mockClear()
    // Registered once per process, by design (mirrors the single `main.tsx` call site) — every test
    // below calls it again anyway to prove the guard makes that safe.
    registerOutboxLogoutQuarantine()
  })

  it('quarantines the outgoing owner exactly once when a session logs out', async () => {
    useAuthStore.getState().login(SESSION_A)
    useAuthStore.getState().logout()

    await vi.waitFor(() => expect(quarantineOwnerBlobs).toHaveBeenCalledTimes(1))
    expect(quarantineOwnerBlobs).toHaveBeenCalledWith(expect.anything(), { userId: 'user-a', tenantId: 'tenant-1' })
  })

  it('does not quarantine anything on login — only on the authenticated -> unauthenticated transition', async () => {
    useAuthStore.getState().login(SESSION_A)

    // Give any stray microtask a turn, then assert nothing fired.
    await Promise.resolve()
    expect(quarantineOwnerBlobs).not.toHaveBeenCalled()
  })

  it('captures the correct owner across a login -> logout -> login-as-someone-else -> logout sequence', async () => {
    useAuthStore.getState().login(SESSION_A)
    useAuthStore.getState().logout()
    await vi.waitFor(() => expect(quarantineOwnerBlobs).toHaveBeenCalledTimes(1))

    useAuthStore.getState().login(SESSION_B)
    useAuthStore.getState().logout()
    await vi.waitFor(() => expect(quarantineOwnerBlobs).toHaveBeenCalledTimes(2))

    expect(quarantineOwnerBlobs).toHaveBeenNthCalledWith(1, expect.anything(), {
      userId: 'user-a',
      tenantId: 'tenant-1',
    })
    expect(quarantineOwnerBlobs).toHaveBeenNthCalledWith(2, expect.anything(), {
      userId: 'user-b',
      tenantId: 'tenant-2',
    })
  })

  it('a second (and third) registration call is a safe no-op — one logout still triggers exactly one sweep', async () => {
    registerOutboxLogoutQuarantine()
    registerOutboxLogoutQuarantine()

    useAuthStore.getState().login(SESSION_A)
    useAuthStore.getState().logout()

    await vi.waitFor(() => expect(quarantineOwnerBlobs).toHaveBeenCalledTimes(1))
  })

  it('calling logout while already logged out does not trigger a spurious quarantine', async () => {
    useAuthStore.getState().logout() // already logged out from beforeEach — a redundant call

    await Promise.resolve()
    expect(quarantineOwnerBlobs).not.toHaveBeenCalled()
  })
})
