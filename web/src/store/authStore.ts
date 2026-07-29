import { create } from 'zustand'

/**
 * Roles issued by the backend JWT (S2-BE-01, `CMPlus.Domain.Enums.UserRole`). Keep in sync with
 * `backend/src/CMPlus.Domain/Enums/UserRole.cs` — adding a role there means adding it here too.
 * Serialized as the enum *name* (project-wide `JsonStringEnumConverter`, Program.cs), never a
 * number.
 */
export type UserRole = 'PM' | 'Planning' | 'Site' | 'QS' | 'Executive' | 'Admin' | 'ProjectDirector'

/**
 * Claims needed for client-side UI/routing decisions only (e.g. `RequireRole`). This is a UX
 * convenience, never a stand-in for server-side authorization — every mutating/reading endpoint
 * re-checks the role and tenant from the JWT itself.
 */
export interface AuthClaims {
  userId: string
  tenantId: string
  role: UserRole
}

export interface AuthSession extends AuthClaims {
  accessToken: string
  /** ISO 8601 instant (`LoginResult.ExpiresAt`, a `DateTimeOffset`). */
  expiresAt: string
}

interface AuthState {
  accessToken: string | null
  expiresAt: string | null
  claims: AuthClaims | null
  isAuthenticated: boolean
  /** Sets the full session after a successful `POST /api/v1/auth/login`. */
  login: (session: AuthSession) => void
  /**
   * Clears the session client-side. There is no server logout endpoint (JWTs are stateless) —
   * this only drops what this tab is holding in memory. Called directly (manual sign-out) or by
   * `apiClient.ts`'s response interceptor on a 401 from an already-authenticated request.
   */
  logout: () => void
}

const emptySession = {
  accessToken: null,
  expiresAt: null,
  claims: null,
  isAuthenticated: false,
} satisfies Omit<AuthState, 'login' | 'logout'>

/**
 * S2-FE-01 auth store (Zustand — UI-routing state only, per the DoD's "no server data mirrored
 * into Zustand" rule; actual domain data is React Query's job from Sprint 4 onward).
 *
 * Token storage decision (`docs/security/secrets-policy.md` is silent on *where* a client-side
 * JWT should live — it only lists "JWT key handling and token storage" as a review checkpoint at
 * S2-SEC-01, without prescribing a location): this store is **in-memory only, never persisted**.
 * No `persist` middleware, no `localStorage`/`sessionStorage` read or write anywhere in this
 * file. Rationale: an XSS payload that can read `localStorage` can read the token for its entire
 * validity window; a token that lives only in JS heap memory is gone the instant the tab reloads
 * or is closed, shrinking the exfiltration window to "while this exact page instance is open."
 *
 * Tradeoff, stated explicitly for `security-auditor` to confirm or correct at S2-SEC-01: a full
 * page reload always requires re-login — no session survives a refresh. If that UX cost is later
 * judged unacceptable, the documented alternative is a backend-issued httpOnly + Secure +
 * SameSite=Strict refresh cookie, never storing the access token itself in `localStorage`.
 */
export const useAuthStore = create<AuthState>((set) => ({
  ...emptySession,
  login: (session) =>
    set({
      accessToken: session.accessToken,
      expiresAt: session.expiresAt,
      claims: { userId: session.userId, tenantId: session.tenantId, role: session.role },
      isAuthenticated: true,
    }),
  logout: () => set({ ...emptySession }),
}))
