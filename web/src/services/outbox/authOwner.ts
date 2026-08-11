import { useAuthStore } from '../../store/authStore'
import type { OutboxOwner } from './types'

/**
 * Resolves the current session's `OutboxOwner` from the shared auth store — the default `getOwner`
 * every outbox-backed feature hook should pass to `createOutboxStore` (S12-FE-01's
 * `usePhotoOutbox.ts`, and S13-FE-01's weather-log/progress-batch hooks alike), so ownership
 * resolution is defined once here rather than duplicated per `kind`.
 *
 * Reads `useAuthStore.getState()` directly (never a subscribed/memoized value) since
 * `outboxStore.ts`'s business logic calls this synchronously and expects the answer to reflect
 * *this exact call's* session — see `CreateOutboxStoreOptions.getOwner`'s own doc comment on why
 * that must never be cached at store-construction time (H-02, Sprint 12 security review).
 */
export function getCurrentOutboxOwner(): OutboxOwner | null {
  const claims = useAuthStore.getState().claims
  return claims ? { userId: claims.userId, tenantId: claims.tenantId } : null
}
