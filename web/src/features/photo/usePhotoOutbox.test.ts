import 'fake-indexeddb/auto'
// See `services/outbox/storage.test.ts`'s identical import comment: jsdom's `Blob` is not the class
// Node's `structuredClone` (which fake-indexeddb clones stored values with) recognises, so every
// blob that actually gets written into the real (fake-indexeddb-polyfilled) IndexedDB store in this
// file must be Node's native `Blob`, not the jsdom global.
import { Blob as NodeBlob } from 'node:buffer'
import { act, renderHook, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { usePhotoOutbox } from './usePhotoOutbox'
import { compressPhotoFile } from './compression'
import { uploadPhoto } from './api'
import { useAuthStore } from '../../store/authStore'
import type { AuthSession } from '../../store/authStore'

vi.mock('./compression', () => ({ compressPhotoFile: vi.fn() }))
vi.mock('./api', () => ({
  uploadPhoto: vi.fn(),
  PhotoApiError: class PhotoApiError extends Error {},
}))

/** Default signed-in session for every test that does not care about *who* is signed in — only that
 * `enqueue` (which now requires an owner, H-02 fix) has one. Tests that specifically exercise
 * ownership scoping switch sessions explicitly with `useAuthStore.getState().login/logout`. */
const OWNER_A_SESSION: AuthSession = {
  accessToken: 'jwt-a',
  expiresAt: '2027-01-01T00:00:00+07:00',
  userId: 'user-a',
  tenantId: 'tenant-1',
  role: 'Site',
}
const OWNER_B_SESSION: AuthSession = {
  accessToken: 'jwt-b',
  expiresAt: '2027-01-01T00:00:00+07:00',
  userId: 'user-b',
  tenantId: 'tenant-1',
  role: 'Site',
}

/** `usePhotoOutbox` always opens the real (fake-indexeddb-polyfilled) `cmplus-outbox` database under
 * its fixed production name — deleting it between tests is what keeps these tests isolated without
 * changing the hook's public signature just for testability. */
function resetOutboxDatabase(): Promise<void> {
  return new Promise((resolve) => {
    const request = indexedDB.deleteDatabase('cmplus-outbox')
    request.onsuccess = () => resolve()
    request.onerror = () => resolve()
    request.onblocked = () => resolve()
  })
}

describe('usePhotoOutbox', () => {
  let onLineSpy: ReturnType<typeof vi.spyOn>

  beforeEach(async () => {
    await resetOutboxDatabase()
    vi.mocked(compressPhotoFile).mockReset()
    vi.mocked(uploadPhoto).mockReset()
    // Offline by default so the mount-time auto-sync trigger (`syncTriggers.ts`) never races a
    // test's own explicit assertions — individual tests opt back in with `.mockReturnValue(true)`.
    onLineSpy = vi.spyOn(navigator, 'onLine', 'get').mockReturnValue(false)
    // `store.enqueue` now requires an authenticated owner (H-02 fix) — every test needs *someone*
    // signed in by default; the ownership-scoping tests below switch sessions explicitly.
    useAuthStore.getState().login(OWNER_A_SESSION)
  })

  afterEach(async () => {
    onLineSpy.mockRestore()
    useAuthStore.getState().logout()
    await resetOutboxDatabase()
  })

  it('capture() compresses the file and enqueues a queued item visible in items', async () => {
    const compressedBlob = new NodeBlob(['compressed'], { type: 'image/jpeg' })
    vi.mocked(compressPhotoFile).mockResolvedValueOnce({
      blob: compressedBlob,
      width: 800,
      height: 600,
      orientationApplied: 6,
    })

    const { result } = renderHook(() => usePhotoOutbox('project-1'))
    await waitFor(() => expect(result.current.items).toEqual([]))

    const file = new File(['raw'], 'site.jpg', { type: 'image/jpeg' })
    let succeeded = false
    await act(async () => {
      succeeded = await result.current.capture(file, { activityId: null, caption: 'test', capturedAt: null })
    })

    expect(succeeded).toBe(true)
    expect(compressPhotoFile).toHaveBeenCalledWith(file)
    await waitFor(() => expect(result.current.items).toHaveLength(1))
    expect(result.current.items[0].status).toBe('queued')
    expect(result.current.items[0].blob).toBeInstanceOf(NodeBlob)
    expect(result.current.processingState).toBe('idle')
  })

  it('capture() surfaces a Thai error and sets processingState to error when compression fails', async () => {
    vi.mocked(compressPhotoFile).mockRejectedValueOnce(new Error('compression failed'))

    const { result } = renderHook(() => usePhotoOutbox('project-1'))
    await waitFor(() => expect(result.current.items).toEqual([]))

    const file = new File(['raw'], 'site.jpg', { type: 'image/jpeg' })
    let succeeded = true
    await act(async () => {
      succeeded = await result.current.capture(file, { activityId: null, caption: null, capturedAt: null })
    })

    expect(succeeded).toBe(false)
    expect(result.current.processingState).toBe('error')
    expect(result.current.processingError).toBeTruthy()
    expect(result.current.items).toEqual([])
  })

  it('syncNow() uploads a queued item and marks it synced with the server id', async () => {
    vi.mocked(compressPhotoFile).mockResolvedValueOnce({
      blob: new NodeBlob(['compressed'], { type: 'image/jpeg' }),
      width: 800,
      height: 600,
      orientationApplied: 1,
    })
    vi.mocked(uploadPhoto).mockResolvedValueOnce({
      id: 'server-photo-1',
      projectId: 'project-1',
      activityId: null,
      caption: null,
      contentType: 'image/jpeg',
      fileSizeBytes: 100,
      uploadedByUserId: 'user-1',
      uploadedAt: '2026-07-08T09:00:00.000Z',
      capturedAt: '2026-07-08T09:00:00.000Z',
    })

    const { result } = renderHook(() => usePhotoOutbox('project-1'))
    await waitFor(() => expect(result.current.items).toEqual([]))

    const file = new File(['raw'], 'site.jpg', { type: 'image/jpeg' })
    await act(async () => {
      await result.current.capture(file, { activityId: null, caption: null, capturedAt: null })
    })
    await waitFor(() => expect(result.current.items).toHaveLength(1))

    await act(async () => {
      await result.current.syncNow()
    })

    await waitFor(() => expect(result.current.items[0].status).toBe('synced'))
    expect(result.current.items[0].serverId).toBe('server-photo-1')
  })

  it('syncNow() marks an item failed (not lost) when the upload rejects, and it stays visible', async () => {
    vi.mocked(compressPhotoFile).mockResolvedValueOnce({
      blob: new NodeBlob(['compressed'], { type: 'image/jpeg' }),
      width: 800,
      height: 600,
      orientationApplied: 1,
    })
    vi.mocked(uploadPhoto).mockRejectedValueOnce(new Error('เครือข่ายขัดข้อง'))

    const { result } = renderHook(() => usePhotoOutbox('project-1'))
    await waitFor(() => expect(result.current.items).toEqual([]))

    const file = new File(['raw'], 'site.jpg', { type: 'image/jpeg' })
    await act(async () => {
      await result.current.capture(file, { activityId: null, caption: null, capturedAt: null })
    })
    await waitFor(() => expect(result.current.items).toHaveLength(1))

    await act(async () => {
      await result.current.syncNow()
    })

    await waitFor(() => expect(result.current.items[0].status).toBe('failed'))
    expect(result.current.items[0].lastError).toBe('เครือข่ายขัดข้อง')
  })

  it('auto-syncs a pending item when the device is already online at mount', async () => {
    vi.mocked(compressPhotoFile).mockResolvedValueOnce({
      blob: new NodeBlob(['compressed'], { type: 'image/jpeg' }),
      width: 800,
      height: 600,
      orientationApplied: 1,
    })

    // First mount (offline): enqueue only, no upload attempted.
    const first = renderHook(() => usePhotoOutbox('project-1'))
    await waitFor(() => expect(first.result.current.items).toEqual([]))
    const file = new File(['raw'], 'site.jpg', { type: 'image/jpeg' })
    await act(async () => {
      await first.result.current.capture(file, { activityId: null, caption: null, capturedAt: null })
    })
    await waitFor(() => expect(first.result.current.items).toHaveLength(1))
    first.unmount()

    // Simulate the device coming online, then a fresh mount of the outbox UI (e.g. app reopened) —
    // the mount-time `navigator.onLine` check (`syncTriggers.ts`) should pick up the still-pending
    // item without any manual "sync now" tap.
    onLineSpy.mockReturnValue(true)
    vi.mocked(uploadPhoto).mockResolvedValueOnce({
      id: 'server-photo-2',
      projectId: 'project-1',
      activityId: null,
      caption: null,
      contentType: 'image/jpeg',
      fileSizeBytes: 100,
      uploadedByUserId: 'user-1',
      uploadedAt: '2026-07-08T09:00:00.000Z',
      capturedAt: '2026-07-08T09:00:00.000Z',
    })

    const second = renderHook(() => usePhotoOutbox('project-1'))
    await waitFor(() => expect(second.result.current.items).toHaveLength(1))
    await waitFor(() => expect(second.result.current.items[0].status).toBe('synced'), { timeout: 2000 })
    second.unmount()
  })

  it('reports syncCapability based on feature detection (fallback-only when no Background Sync API)', async () => {
    const { result } = renderHook(() => usePhotoOutbox('project-1'))
    await waitFor(() => expect(result.current.syncCapability).toBe('fallback-only'))
  })

  describe('ownership scoping across sessions (H-02 fix, Sprint 12 security review)', () => {
    it("a photo captured by user A while offline is never visible to, or synced by, user B who signs in next on the same device", async () => {
      // `useAuthStore.getState().login(OWNER_A_SESSION)` already ran in `beforeEach` — captures under
      // A's session.
      vi.mocked(compressPhotoFile).mockResolvedValueOnce({
        blob: new NodeBlob(['compressed'], { type: 'image/jpeg' }),
        width: 800,
        height: 600,
        orientationApplied: 1,
      })

      const sessionA = renderHook(() => usePhotoOutbox('project-1'))
      await waitFor(() => expect(sessionA.result.current.items).toEqual([]))
      const file = new File(['raw'], 'site.jpg', { type: 'image/jpeg' })
      await act(async () => {
        await sessionA.result.current.capture(file, {
          activityId: null,
          caption: "defect at column C4 - A's private note",
          capturedAt: null,
        })
      })
      await waitFor(() => expect(sessionA.result.current.items).toHaveLength(1))
      expect(sessionA.result.current.items[0].status).toBe('queued')
      sessionA.unmount()

      // The shared-tablet handoff: A signs out, B signs in — a fresh mount, exactly like navigating
      // back through /login. B's device is online (the worst case: a mount-time auto-sync attempt).
      useAuthStore.getState().logout()
      useAuthStore.getState().login(OWNER_B_SESSION)
      onLineSpy.mockReturnValue(true)

      const sessionB = renderHook(() => usePhotoOutbox('project-1'))
      await waitFor(() => expect(sessionB.result.current.syncCapability).toBe('fallback-only'))

      // Confidentiality: B's own outbox view never contains A's item or caption.
      expect(sessionB.result.current.items).toEqual([])

      // Availability/audit integrity: neither the mount-time auto-sync nor an explicit "sync now"
      // tap on B's session may upload A's photo on B's token.
      await act(async () => {
        await sessionB.result.current.syncNow()
      })
      expect(uploadPhoto).not.toHaveBeenCalled()
      sessionB.unmount()

      // A signs back in later: the item was never lost, only correctly hidden while B was signed in.
      useAuthStore.getState().logout()
      useAuthStore.getState().login(OWNER_A_SESSION)
      onLineSpy.mockReturnValue(false)

      const sessionA2 = renderHook(() => usePhotoOutbox('project-1'))
      await waitFor(() => expect(sessionA2.result.current.items).toHaveLength(1))
      expect(sessionA2.result.current.items[0].status).toBe('queued')
      expect(sessionA2.result.current.items[0].payload.fields.caption).toBe("defect at column C4 - A's private note")
      sessionA2.unmount()
    })

    it('capture() throws (does not silently enqueue) when nobody is authenticated', async () => {
      useAuthStore.getState().logout()
      vi.mocked(compressPhotoFile).mockResolvedValueOnce({
        blob: new NodeBlob(['compressed'], { type: 'image/jpeg' }),
        width: 800,
        height: 600,
        orientationApplied: 1,
      })

      const { result } = renderHook(() => usePhotoOutbox('project-1'))
      await waitFor(() => expect(result.current.items).toEqual([]))

      const file = new File(['raw'], 'site.jpg', { type: 'image/jpeg' })
      let succeeded = true
      await act(async () => {
        succeeded = await result.current.capture(file, { activityId: null, caption: null, capturedAt: null })
      })

      expect(succeeded).toBe(false)
      expect(result.current.processingState).toBe('error')
      expect(result.current.items).toEqual([])
    })
  })

  describe("L-06: the displayed queue is scoped to this route's projectId", () => {
    it("does not show an item captured for a different project, even under the same owner and device", async () => {
      vi.mocked(compressPhotoFile).mockResolvedValue({
        blob: new NodeBlob(['compressed'], { type: 'image/jpeg' }),
        width: 800,
        height: 600,
        orientationApplied: 1,
      })

      const projectA = renderHook(() => usePhotoOutbox('project-A'))
      await waitFor(() => expect(projectA.result.current.items).toEqual([]))
      await act(async () => {
        await projectA.result.current.capture(new File(['raw'], 'a.jpg', { type: 'image/jpeg' }), {
          activityId: null,
          caption: 'for project A',
          capturedAt: null,
        })
      })
      await waitFor(() => expect(projectA.result.current.items).toHaveLength(1))
      projectA.unmount()

      const projectB = renderHook(() => usePhotoOutbox('project-B'))
      await waitFor(() => expect(projectB.result.current.items).toEqual([]))
      await act(async () => {
        await projectB.result.current.capture(new File(['raw'], 'b.jpg', { type: 'image/jpeg' }), {
          activityId: null,
          caption: 'for project B',
          capturedAt: null,
        })
      })
      await waitFor(() => expect(projectB.result.current.items).toHaveLength(1))
      expect(projectB.result.current.items[0].payload.fields.caption).toBe('for project B')
      projectB.unmount()

      // Remount project A's screen — same owner, same device-wide IndexedDB database — must show
      // only project A's item, never project B's.
      const projectAAgain = renderHook(() => usePhotoOutbox('project-A'))
      await waitFor(() => expect(projectAAgain.result.current.items).toHaveLength(1))
      expect(projectAAgain.result.current.items[0].payload.fields.caption).toBe('for project A')
      projectAAgain.unmount()
    })
  })
})
