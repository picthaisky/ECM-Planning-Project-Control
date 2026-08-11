import type { Page, Route } from '@playwright/test'

/**
 * Shared fixtures/helpers for S12-QA-01 (`docs/10.` Sprint 12, qa-engineer row) — the offline→sync
 * E2E for Photo Progress (`web/e2e/photo-offline.spec.ts`).
 *
 * **No real backend is used.** Docker cannot start on this workstation (needs Administrator — see
 * `docs/perf/gantt-frontend-s6.md` §3 for the identical constraint hit in Sprint 6), so there is no
 * running `docker-api-1`/`docker-mssql-1`. What this suite actually needs is a real browser with
 * real IndexedDB, real `context.setOffline()` network emulation, and real canvas/`createImageBitmap`
 * decode — none of which need a live backend. Every HTTP call this suite would otherwise make is
 * intercepted at the Playwright network layer (`page.route`, which hooks Chromium's `Fetch` domain
 * before a request ever reaches a socket) and answered by the fake handlers below. This is
 * deliberately narrower than a full integration test — it proves *client* outbox/sync behaviour
 * (the offline queue, idempotency-key stability, retry/dedup contract, UI status truthfulness),
 * which is exactly what this task's DoD ("ไม่มีรูปหาย ไม่มีรูปซ้ำ") is actually about. It does NOT
 * prove the real backend honours `Idempotency-Key` end-to-end — that middleware does not exist yet
 * (`docs/10.` S13-BE-01, Sprint 13); see this file's `installMockPhotoBackend` doc comment and the
 * spec's own remarks on that residual gap.
 */

/** Any syntactically valid GUID works — `SelectProjectPage.tsx` only checks the shape client-side,
 * and every backend call this suite makes is mocked, so no real project needs to exist server-side. */
export const FAKE_PROJECT_ID = '3fa85f64-5717-4562-b3fc-2c963f66afa6'

/**
 * A mock login identity — `installMockPhotoBackend`'s `**\/api/v1/auth/login` handler looks up the
 * posted `email` against a small registry of these (see `IDENTITIES` below) and returns the matching
 * `userId`/`tenantId`/`role`, rather than always returning one fixed identity regardless of
 * credentials. This is what makes a real logout -> login-as-someone-else E2E scenario possible
 * (H-02, Sprint 12 security review) — every earlier test in this suite that only ever needed "a"
 * user still works unchanged via `DEV_USER` (`= DEV_USER_A`).
 */
export interface FakeIdentity {
  email: string
  password: string
  userId: string
  tenantId: string
  role: string
}

export const DEV_USER_A: FakeIdentity = {
  email: 'site.foreman.a@cmplus.test',
  password: 'not-checked-mock-backend',
  userId: '1b9d6bcd-bbfd-4b2d-9b5d-ab8dfbbd4bed',
  tenantId: '9c858901-8a57-4791-81fe-4c455b099bc9',
  role: 'Site',
}

/** A second identity in a *different* tenant — the harsher of the two shared-device handoff cases
 * H-02 names (same-tenant-different-user, and cross-tenant). */
export const DEV_USER_B: FakeIdentity = {
  email: 'site.foreman.b@cmplus.test',
  password: 'not-checked-mock-backend',
  userId: '6634bad1-7702-4471-8a7a-38a29183c9a4',
  tenantId: '28064a9f-137c-4e5c-b125-42fe92d003fd',
  role: 'Site',
}

const IDENTITIES: readonly FakeIdentity[] = [DEV_USER_A, DEV_USER_B]

/** Back-compat alias — every test written before the multi-identity support above logs in as this
 * one identity, unchanged. */
export const DEV_USER = DEV_USER_A

/** One recorded attempt at `POST /projects/{id}/photos`, in arrival order. */
export interface PhotoUploadAttempt {
  /** 1-based, across the whole mock's lifetime (all photos), so tests can reason about global
   * request ordering/interleaving, not just per-key counts. */
  requestNumber: number
  idempotencyKey: string | null
  bodyBytes: number
  /** The `userId` recovered from the request's `Authorization: Bearer` token (see `installMockPhotoBackend`'s
   * login handler, which mints `e2e-fake-jwt-<userId>`) — i.e. *whoever's session actually fired this
   * HTTP request*, independent of which identity originally captured/queued the photo. This is what
   * the H-02 review's audit-integrity consequence is about: a stranded item flushed under a different
   * session's token gets attributed to the wrong person server-side. `null` if no bearer token was
   * present or it did not match this mock's minting format. */
  uploadedByUserId: string | null
}

export type UploadOutcome =
  | { type: 'success' }
  | { type: 'network-drop' } // the real "lost response" case: the fake server still records the
  // attempt (see below) before aborting the connection, simulating a request that reached the
  // server and could have been acted on, but whose reply never made it back to the client.
  | { type: 'http-error'; status: number }

export interface FakePhotoBackend {
  /** Every attempt, in arrival order — the ground truth for "how many HTTP requests actually went
   * out", independent of what the client believes happened. */
  attempts: PhotoUploadAttempt[]
  /**
   * `idempotencyKey -> serverId` for keys the fake server has assigned an id to. Only populated
   * when `dedupeByIdempotencyKey: true` was passed to `installMockPhotoBackend` — mirrors the
   * *not-yet-shipped* S13-BE-01 contract. With dedup **off** (the default, matching today's real
   * backend, which ignores the `Idempotency-Key` header entirely — see `features/photo/api.ts`'s
   * own remarks), every successful attempt mints a fresh server id even when the key repeats.
   */
  createdServerIdsByKey: Map<string, string>
  /** Distinct server ids actually minted — with dedup off this can exceed the number of distinct
   * idempotency keys seen; that gap *is* the "would this be a duplicate photo in production today"
   * signal a test can assert on directly. */
  mintedServerIds: string[]
}

export interface InstallMockPhotoBackendOptions {
  /** Decide what happens to each arriving upload attempt. Defaults to always `{ type: 'success' }`. */
  decide?: (info: { idempotencyKey: string | null; requestNumber: number }) => UploadOutcome
  /** See `FakePhotoBackend.createdServerIdsByKey`'s doc comment. Defaults to `false` — the current,
   * real backend's actual behaviour (no idempotency middleware yet). */
  dedupeByIdempotencyKey?: boolean
}

function headerCaseInsensitive(headers: Record<string, string>, name: string): string | null {
  const key = Object.keys(headers).find((h) => h.toLowerCase() === name.toLowerCase())
  return key ? headers[key] : null
}

const BEARER_TOKEN_PREFIX = 'e2e-fake-jwt-'

/** Recovers the `userId` embedded in a mock-minted `Authorization: Bearer e2e-fake-jwt-<userId>`
 * header — see this file's own remarks on `PhotoUploadAttempt.uploadedByUserId`. */
function userIdFromAuthHeader(headers: Record<string, string>): string | null {
  const authHeader = headerCaseInsensitive(headers, 'authorization')
  if (!authHeader?.startsWith('Bearer ')) return null
  const token = authHeader.slice('Bearer '.length)
  return token.startsWith(BEARER_TOKEN_PREFIX) ? token.slice(BEARER_TOKEN_PREFIX.length) : null
}

/**
 * Installs `POST /api/v1/auth/login` and `POST /api/v1/projects/{id}/photos` mocks on `page`.
 * Must be called before any navigation that would trigger those requests (`page.route` registers
 * for the page's whole lifetime, including reloads — no need to re-install after `page.reload()`).
 */
export async function installMockPhotoBackend(
  page: Page,
  options: InstallMockPhotoBackendOptions = {},
): Promise<FakePhotoBackend> {
  const decide = options.decide ?? (() => ({ type: 'success' as const }))
  const dedupe = options.dedupeByIdempotencyKey ?? false

  const backend: FakePhotoBackend = {
    attempts: [],
    createdServerIdsByKey: new Map(),
    mintedServerIds: [],
  }
  let requestCounter = 0

  await page.route('**/api/v1/auth/login', async (route: Route) => {
    const request = route.request()
    let email: string | undefined
    try {
      email = (request.postDataJSON() as { email?: string } | undefined)?.email
    } catch {
      email = undefined
    }
    // Falls back to `DEV_USER_A` for any credentials this registry does not recognise — every test
    // written before multi-identity support (i.e. everything using the plain `DEV_USER` alias) keeps
    // working unchanged.
    const identity = IDENTITIES.find((candidate) => candidate.email === email) ?? DEV_USER_A

    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        accessToken: `${BEARER_TOKEN_PREFIX}${identity.userId}`,
        expiresAt: new Date(Date.now() + 3_600_000).toISOString(),
        userId: identity.userId,
        tenantId: identity.tenantId,
        role: identity.role,
      }),
    })
  })

  await page.route('**/api/v1/projects/*/photos', async (route: Route) => {
    const request = route.request()
    if (request.method() !== 'POST') {
      await route.continue()
      return
    }

    requestCounter += 1
    const idempotencyKey = headerCaseInsensitive(request.headers(), 'idempotency-key')
    const bodyBytes = request.postDataBuffer()?.length ?? 0
    const uploadedByUserId = userIdFromAuthHeader(request.headers())
    backend.attempts.push({ requestNumber: requestCounter, idempotencyKey, bodyBytes, uploadedByUserId })

    const outcome = decide({ idempotencyKey, requestNumber: requestCounter })

    if (outcome.type === 'network-drop') {
      // The fake server still "processes" the request (as a real backend, absent S13-BE-01 dedup,
      // would create a row) before the reply is lost — recorded via mintedServerIds even though the
      // client never sees this id. Whether a *retry* reuses or duplicates this depends on `dedupe`.
      if (!(dedupe && idempotencyKey && backend.createdServerIdsByKey.has(idempotencyKey))) {
        const serverId = `photo-${requestCounter}`
        backend.mintedServerIds.push(serverId)
        if (idempotencyKey) backend.createdServerIdsByKey.set(idempotencyKey, serverId)
      }
      await route.abort('connectionreset')
      return
    }

    if (outcome.type === 'http-error') {
      await route.fulfill({
        status: outcome.status,
        contentType: 'application/problem+json',
        body: JSON.stringify({
          type: 'https://cmplus.dev/errors/mock-upload-error',
          title: 'Mock upload failure',
          status: outcome.status,
          detail: 'mock-photo-upload-error',
        }),
      })
      return
    }

    let serverId: string
    if (dedupe && idempotencyKey && backend.createdServerIdsByKey.has(idempotencyKey)) {
      serverId = backend.createdServerIdsByKey.get(idempotencyKey) as string
    } else {
      serverId = `photo-${requestCounter}`
      backend.mintedServerIds.push(serverId)
      if (idempotencyKey) backend.createdServerIdsByKey.set(idempotencyKey, serverId)
    }

    await route.fulfill({
      status: 201,
      contentType: 'application/json',
      body: JSON.stringify({
        id: serverId,
        projectId: FAKE_PROJECT_ID,
        activityId: null,
        caption: null,
        contentType: 'image/jpeg',
        fileSizeBytes: bodyBytes,
        uploadedByUserId: uploadedByUserId ?? DEV_USER_A.userId,
        uploadedAt: new Date().toISOString(),
        capturedAt: new Date().toISOString(),
      }),
    })
  })

  return backend
}

/**
 * Logs in against the mocked backend and lands on the Photo Progress screen
 * (`/app/:projectId/photo`), via real UI interaction only (never `page.goto` post-login — the
 * in-memory-only JWT in `authStore.ts` would be silently dropped by a full navigation, exactly as
 * `support/gantt.ts#loginAndOpenGantt` documents).
 *
 * Includes the same app-identity assertion `support/gantt.ts` established after Sprint 6's "drove
 * an entirely different application on the wrong port" incident — cheap insurance against the same
 * failure class here, not an assumption that the page under test is ours.
 *
 * `identity` defaults to `DEV_USER` (`= DEV_USER_A`) — pass `DEV_USER_B` for the shared-device
 * "someone else signs in next" scenarios (H-02, Sprint 12 security review).
 */
export async function loginAndOpenPhotoPage(
  page: Page,
  projectId: string = FAKE_PROJECT_ID,
  identity: FakeIdentity = DEV_USER,
): Promise<void> {
  await page.goto('/login')

  await page
    .getByRole('heading', { name: 'CM+ Project Control' })
    .waitFor({ state: 'visible', timeout: 15_000 })
    .catch(() => {
      throw new Error(
        `Expected the CM+ login page at ${page.url()}, but its identifying heading was never found. ` +
          'Something other than this app is almost certainly answering that port.',
      )
    })

  await page.getByLabel('อีเมล').fill(identity.email)
  await page.getByLabel('รหัสผ่าน').fill(identity.password)
  await page.getByRole('button', { name: 'เข้าสู่ระบบ' }).click()

  await page.waitForURL((url) => url.pathname === '/select-project' || url.pathname.startsWith('/app/'), {
    timeout: 15_000,
  })

  if (new URL(page.url()).pathname === '/select-project') {
    await page.getByLabel('รหัสโครงการ (Project ID)').fill(projectId)
    await page.getByRole('button', { name: 'เข้าสู่โครงการ' }).click()
  }

  await page.waitForURL(`**/app/${projectId}/**`, { timeout: 15_000 })
  // A remembered project (`projectStore.ts`, localStorage-persisted — deliberately *not* cleared by
  // logout, see that file's own remarks) can land a freshly-logged-in session directly on a *different*
  // screen than Photo Progress (e.g. still on the previous session's last-open tab). Navigate there
  // explicitly rather than assuming the redirect already landed on it.
  if (new URL(page.url()).pathname !== `/app/${projectId}/photo`) {
    await page.getByRole('link', { name: 'Photo Progress' }).click()
    await page.waitForURL(`**/app/${projectId}/photo`)
  }
  await page.getByLabel('รูปภาพ').waitFor({ state: 'visible', timeout: 15_000 })
}

/**
 * Signs out via the real Sidebar "ออกจากระบบ" button — the same control a site engineer would use to
 * hand a shared tablet to the next person (H-02, Sprint 12 security review) — and waits for the
 * `RequireAuth` redirect back to `/login` to complete.
 */
export async function logout(page: Page): Promise<void> {
  await page.getByRole('button', { name: 'ออกจากระบบ' }).click()
  await page.waitForURL('**/login**', { timeout: 15_000 })
}

/** Absolute path to a real, valid JPEG fixture (300x200, EXIF Orientation=6, top-half red /
 * bottom-half blue) used to prove EXIF orientation is genuinely baked into output pixels — a
 * real-browser-only check (`compression.ts`'s own remarks: jsdom has neither canvas nor
 * `createImageBitmap`). Generated once via `System.Drawing` (see git history/PR description for the
 * generator script) — checked in like any other binary test fixture. */
export const ORIENTED_PHOTO_FIXTURE = 'e2e/fixtures/site-photo-oriented.jpg'
/** Small, ordinary photo (no special EXIF) for tests that just need "a real, valid image file". */
export const SMALL_PHOTO_FIXTURE = 'e2e/fixtures/site-photo-small.jpg'
/** Large (~3.2 MB), detailed, photo-like JPEG — used to prove the client actually compresses down
 * toward the ~300 KB DoD target rather than passing the original through. */
export const LARGE_PHOTO_FIXTURE = 'e2e/fixtures/site-photo-large.jpg'

/**
 * Reads every item currently in the real (browser-native) `cmplus-outbox` IndexedDB database,
 * entirely inside the page — this is the "did it actually survive in IndexedDB, not just React
 * state" check the DoD requires, so it must not go through `usePhotoOutbox`'s React state at all.
 */
export async function readOutboxItemsFromIndexedDb(page: Page): Promise<
  Array<{
    id: string
    kind: string
    status: string
    idempotencyKey: string
    hasBlob: boolean
    blobSize: number
    ownerUserId: string | undefined
    ownerTenantId: string | undefined
  }>
> {
  return page.evaluate(async () => {
    const db: IDBDatabase = await new Promise((resolve, reject) => {
      const req = indexedDB.open('cmplus-outbox')
      req.onsuccess = () => resolve(req.result)
      req.onerror = () => reject(req.error)
    })
    const items: unknown[] = await new Promise((resolve, reject) => {
      const tx = db.transaction('items', 'readonly')
      const store = tx.objectStore('items')
      const req = store.getAll()
      req.onsuccess = () => resolve(req.result)
      req.onerror = () => reject(req.error)
    })
    db.close()
    return (items as Array<Record<string, unknown>>).map((item) => ({
      id: item.id as string,
      kind: item.kind as string,
      status: item.status as string,
      idempotencyKey: item.idempotencyKey as string,
      hasBlob: item.blob != null,
      blobSize: item.blob instanceof Blob ? (item.blob as Blob).size : 0,
      // `ownerUserId`/`ownerTenantId` (H-02 fix) are `undefined` on a real pre-fix legacy record —
      // read as plain properties (not cast away) so tests can assert on that shape directly.
      ownerUserId: item.ownerUserId as string | undefined,
      ownerTenantId: item.ownerTenantId as string | undefined,
    }))
  })
}

/**
 * Decodes the compressed blob of the single queued/failed photo outbox item directly inside the
 * page (real canvas + `createImageBitmap`, unavailable in this repo's jsdom unit tests) and returns
 * its pixel dimensions plus a couple of sampled pixel colours — enough to prove EXIF orientation was
 * baked into the actual output pixels, not merely recorded as metadata.
 */
export async function inspectQueuedPhotoBlob(
  page: Page,
): Promise<{ width: number; height: number; sizeBytes: number; leftPixel: [number, number, number]; rightPixel: [number, number, number] }> {
  return page.evaluate(async () => {
    const db: IDBDatabase = await new Promise((resolve, reject) => {
      const req = indexedDB.open('cmplus-outbox')
      req.onsuccess = () => resolve(req.result)
      req.onerror = () => reject(req.error)
    })
    const items: Array<{ blob: Blob; status: string }> = await new Promise((resolve, reject) => {
      const tx = db.transaction('items', 'readonly')
      const store = tx.objectStore('items')
      const req = store.getAll()
      req.onsuccess = () => resolve(req.result)
      req.onerror = () => reject(req.error)
    })
    db.close()

    const target = items.find((item) => item.blob != null)
    if (!target) throw new Error('No outbox item with a stored blob was found')

    const bitmap = await createImageBitmap(target.blob)
    const canvas = document.createElement('canvas')
    canvas.width = bitmap.width
    canvas.height = bitmap.height
    const ctx = canvas.getContext('2d')
    if (!ctx) throw new Error('2D context unavailable')
    ctx.drawImage(bitmap, 0, 0)

    // Sample near the left edge and right edge, vertically centered — enough to distinguish the
    // fixture's red/blue halves regardless of which axis they ended up on after orientation.
    const leftData = ctx.getImageData(Math.max(1, Math.floor(bitmap.width * 0.25)), Math.floor(bitmap.height / 2), 1, 1).data
    const rightData = ctx.getImageData(Math.floor(bitmap.width * 0.75), Math.floor(bitmap.height / 2), 1, 1).data

    return {
      width: bitmap.width,
      height: bitmap.height,
      sizeBytes: target.blob.size,
      leftPixel: [leftData[0], leftData[1], leftData[2]],
      rightPixel: [rightData[0], rightData[1], rightData[2]],
    }
  })
}
