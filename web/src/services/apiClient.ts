import axios, { AxiosError } from 'axios'
import type { InternalAxiosRequestConfig } from 'axios'
import { useAuthStore } from '../store/authStore'
import { pushToast } from '../store/toastStore'

/**
 * Relative by default so the same production build works behind whatever reverse proxy fronts
 * `/api` in each environment, without a rebuild per environment. In local dev, Vite's own dev
 * server proxies `/api` to the real API (see `vite.config.ts` `server.proxy` +
 * `web/.env.example`'s `API_PROXY_TARGET`) — a same-origin, server-side hop, so the browser never
 * makes a cross-origin request and no CORS policy is needed on the API for local development.
 * Override only if `/api` is not reachable under the current origin (not a secret — see
 * `docs/security/secrets-policy.md`'s VITE_* rule — but there is no secret value to put here).
 */
export const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? '/api/v1'

/**
 * Requests whose 401 must never trigger the global "session expired" flow. A wrong password on
 * the login screen itself is an expected, locally-handled failure (`features/auth/api.ts` turns
 * it into a `LoginError` the form displays inline) — it is not an expired/invalid session and
 * must not clear the store or redirect.
 */
const SESSION_EXPIRY_EXEMPT_PATHS = ['/auth/login']

function isExemptFromSessionExpiry(url: string | undefined): boolean {
  if (!url) return false
  return SESSION_EXPIRY_EXEMPT_PATHS.some((path) => url.includes(path))
}

/**
 * Request interceptor: attaches `Authorization: Bearer <token>` from the in-memory auth store, if
 * a session exists. Exported standalone (not only as an anonymous `.use()` callback) so it is
 * unit-testable without an actual HTTP request.
 */
export function attachAuthHeader(config: InternalAxiosRequestConfig): InternalAxiosRequestConfig {
  const token = useAuthStore.getState().accessToken
  if (token) {
    config.headers.set('Authorization', `Bearer ${token}`)
  }
  return config
}

/**
 * Response interceptor: on a 401 from an *already-authenticated* request (never from the login
 * attempt itself, see `isExemptFromSessionExpiry`), clears the auth store, shows a toast, and
 * hard-redirects to `/login`.
 *
 * A hard redirect (`window.location.assign`, not client-side navigation) is deliberate given the
 * in-memory-only token model (`authStore.ts`): it guarantees a full reload, so no stale component
 * anywhere in the tree can keep holding the expired session in props/closures after the store is
 * cleared.
 */
export function handleResponseError(error: AxiosError): Promise<never> {
  const status = error.response?.status
  const requestUrl = error.config?.url

  if (status === 401 && !isExemptFromSessionExpiry(requestUrl) && useAuthStore.getState().isAuthenticated) {
    useAuthStore.getState().logout()
    pushToast({ message: 'เซสชันหมดอายุ กรุณาเข้าสู่ระบบใหม่' })

    if (typeof window !== 'undefined' && window.location.pathname !== '/login') {
      window.location.assign('/login')
    }
  }

  return Promise.reject(error)
}

/** Shared Axios instance — every `src/features/<module>/api.ts` module calls through this, never
 * a bare `axios` import, so the auth header and 401 handling are never accidentally skipped. */
export const apiClient = axios.create({ baseURL: API_BASE_URL })

apiClient.interceptors.request.use(attachAuthHeader)
apiClient.interceptors.response.use((response) => response, handleResponseError)
