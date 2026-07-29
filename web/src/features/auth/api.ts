import { AxiosError } from 'axios'
import { apiClient } from '../../services/apiClient'
import type { LoginRequest, LoginResponse, ProblemDetails } from './types'

/**
 * Thrown by `login()` with a Thai-first message ready to render directly in the UI — callers
 * never need to inspect the raw ProblemDetails/Axios error themselves.
 */
export class LoginError extends Error {
  readonly status?: number

  constructor(message: string, status?: number) {
    super(message)
    this.name = 'LoginError'
    this.status = status
  }
}

const INVALID_CREDENTIALS_MESSAGE = 'อีเมลหรือรหัสผ่านไม่ถูกต้อง'
const GENERIC_ERROR_MESSAGE = 'เข้าสู่ระบบไม่สำเร็จ กรุณาลองใหม่อีกครั้ง'

/**
 * `POST /api/v1/auth/login` (US-2.1, S2-BE-01). Never resolves with a partial/failed session —
 * throws `LoginError` with Thai copy the `LoginPage` can render as-is. The backend intentionally
 * returns the same generic failure whether the email is unknown or the password is wrong
 * (`LoginCommandHandler` — never reveals which, so login cannot enumerate registered emails); the
 * UI mirrors that by showing one generic message for any 401.
 */
export async function login(request: LoginRequest): Promise<LoginResponse> {
  try {
    const response = await apiClient.post<LoginResponse>('/auth/login', request)
    return response.data
  } catch (error) {
    if (error instanceof AxiosError) {
      const status = error.response?.status

      if (status === 401) {
        throw new LoginError(INVALID_CREDENTIALS_MESSAGE, status)
      }

      const problem = error.response?.data as ProblemDetails | undefined
      throw new LoginError(problem?.detail ?? problem?.title ?? GENERIC_ERROR_MESSAGE, status)
    }

    throw new LoginError(GENERIC_ERROR_MESSAGE)
  }
}
