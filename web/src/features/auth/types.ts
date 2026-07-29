import type { UserRole } from '../../store/authStore'

export type { UserRole }

/** `POST /api/v1/auth/login` request body (`LoginCommand`, S2-BE-01). */
export interface LoginRequest {
  email: string
  password: string
}

/**
 * `POST /api/v1/auth/login` 200 response body (`LoginResult`). Property names/casing mirror the
 * backend's camelCase JSON exactly (Program.cs's default `System.Text.Json` options + the
 * project-wide `JsonStringEnumConverter`, so `role` arrives as e.g. `"Admin"`, never a number).
 */
export interface LoginResponse {
  accessToken: string
  expiresAt: string
  userId: string
  tenantId: string
  role: UserRole
}

/**
 * RFC 7807 error body (`GlobalExceptionHandler` / `ResultProblemMapper`) — same camelCase
 * `JsonSerializerOptions` as every other WebApi response.
 */
export interface ProblemDetails {
  type?: string
  title?: string
  status?: number
  detail?: string
  instance?: string
}
