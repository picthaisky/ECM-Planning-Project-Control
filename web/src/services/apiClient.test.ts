import { AxiosError, AxiosHeaders } from 'axios'
import type { AxiosResponse, InternalAxiosRequestConfig } from 'axios'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { attachAuthHeader, handleResponseError, isServedFromOfflineCache } from './apiClient'
import { useAuthStore } from '../store/authStore'
import { useToastStore } from '../store/toastStore'

const sampleSession = {
  accessToken: 'jwt-abc',
  expiresAt: '2026-01-01T00:00:00+07:00',
  userId: 'u1',
  tenantId: 't1',
  role: 'PM' as const,
}

function makeConfig(url: string): InternalAxiosRequestConfig {
  return { url, headers: new AxiosHeaders() } as InternalAxiosRequestConfig
}

function makeError(status: number, url: string): AxiosError {
  const config = makeConfig(url)
  return new AxiosError('Request failed', String(status), config, undefined, {
    status,
    statusText: '',
    data: {},
    headers: {},
    config,
  })
}

describe('apiClient interceptors', () => {
  beforeEach(() => {
    useAuthStore.getState().logout()
    useToastStore.setState({ toasts: [] })
    // window.location.assign is not implemented by jsdom - stub it so handleResponseError's
    // hard-redirect path doesn't spam "Not implemented" console noise or navigate the test page.
    Object.defineProperty(window, 'location', {
      value: { ...window.location, pathname: '/wbs', assign: vi.fn() },
      writable: true,
      configurable: true,
    })
  })

  describe('attachAuthHeader', () => {
    it('does not set an Authorization header when there is no session', () => {
      const result = attachAuthHeader(makeConfig('/wbs/tree'))
      expect(result.headers.get('Authorization')).toBeFalsy()
    })

    it('attaches Bearer <token> when a session exists', () => {
      useAuthStore.getState().login(sampleSession)
      const result = attachAuthHeader(makeConfig('/wbs/tree'))
      expect(result.headers.get('Authorization')).toBe('Bearer jwt-abc')
    })
  })

  describe('handleResponseError', () => {
    it('always rejects with the original error', async () => {
      const error = makeError(500, '/wbs/tree')
      await expect(handleResponseError(error)).rejects.toBe(error)
    })

    it('does not log out or toast for a 401 from the login endpoint itself', async () => {
      useAuthStore.getState().login(sampleSession)
      const error = makeError(401, '/auth/login')

      await expect(handleResponseError(error)).rejects.toBe(error)

      expect(useAuthStore.getState().isAuthenticated).toBe(true)
      expect(useToastStore.getState().toasts).toHaveLength(0)
      expect(window.location.assign).not.toHaveBeenCalled()
    })

    it('clears the store, toasts, and redirects for a 401 on an authenticated request', async () => {
      useAuthStore.getState().login(sampleSession)
      const error = makeError(401, '/wbs/tree')

      await expect(handleResponseError(error)).rejects.toBe(error)

      expect(useAuthStore.getState().isAuthenticated).toBe(false)
      expect(useAuthStore.getState().accessToken).toBeNull()
      expect(useToastStore.getState().toasts).toHaveLength(1)
      expect(useToastStore.getState().toasts[0].message).toContain('เซสชันหมดอายุ')
      expect(window.location.assign).toHaveBeenCalledWith('/login')
    })

    it('does nothing extra for a 401 when there was no session to begin with', async () => {
      const error = makeError(401, '/wbs/tree')

      await expect(handleResponseError(error)).rejects.toBe(error)

      expect(useToastStore.getState().toasts).toHaveLength(0)
      expect(window.location.assign).not.toHaveBeenCalled()
    })
  })

  describe('isServedFromOfflineCache (S13-FE-02)', () => {
    it('is false for an ordinary live response with no such header', () => {
      const response = { headers: {} } as AxiosResponse
      expect(isServedFromOfflineCache(response)).toBe(false)
    })

    it('is true when the service worker\'s cache-fallback header is present', () => {
      const response = { headers: { 'x-cm-served-from': 'sw-cache' } } as unknown as AxiosResponse
      expect(isServedFromOfflineCache(response)).toBe(true)
    })

    it('is false for an unrelated header value (never a loose truthy check)', () => {
      const response = { headers: { 'x-cm-served-from': 'something-else' } } as unknown as AxiosResponse
      expect(isServedFromOfflineCache(response)).toBe(false)
    })
  })
})
