import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { activatePendingUpdate, registerServiceWorker } from './registerServiceWorker'
import { useSwUpdateStore } from '../store/swUpdateStore'
import { useAuthStore } from '../store/authStore'
import type { AuthSession } from '../store/authStore'

/** Minimal, spec-shaped fakes for the browser Service Worker API — none of which jsdom implements —
 * built on the real `EventTarget` (available in jsdom) so `addEventListener`/`dispatchEvent` behave
 * exactly like the real thing rather than needing a hand-rolled pub/sub. */
class FakeServiceWorker extends EventTarget {
  state: string
  constructor(state: string) {
    super()
    this.state = state
  }
  setState(state: string) {
    this.state = state
    this.dispatchEvent(new Event('statechange'))
  }
}

class FakeRegistration extends EventTarget {
  installing: FakeServiceWorker | null = null
  waiting: FakeServiceWorker | null = null
  active: FakeServiceWorker | null = null
  update = vi.fn().mockResolvedValue(undefined)

  /** Simulates the browser starting to install a new worker for this registration. */
  triggerUpdateFound(worker: FakeServiceWorker) {
    this.installing = worker
    this.dispatchEvent(new Event('updatefound'))
  }
}

class FakeServiceWorkerContainer extends EventTarget {
  controller: FakeServiceWorker | null = null
  register = vi.fn()
  getRegistration = vi.fn()
}

const OWNER_SESSION: AuthSession = {
  accessToken: 'jwt',
  expiresAt: '2027-01-01T00:00:00+07:00',
  userId: 'user-1',
  tenantId: 'tenant-1',
  role: 'PM',
}

function installFakeServiceWorkerContainer(): FakeServiceWorkerContainer {
  const container = new FakeServiceWorkerContainer()
  Object.defineProperty(navigator, 'serviceWorker', { value: container, configurable: true, writable: true })
  return container
}

/** Lets a microtask-queued `.then()` (e.g. `register(...).then(...)`) actually run before assertions. */
async function flushMicrotasks(): Promise<void> {
  await Promise.resolve()
  await Promise.resolve()
}

describe('registerServiceWorker (S13-FE-02)', () => {
  beforeEach(() => {
    useSwUpdateStore.setState({ updateAvailable: false })
    useAuthStore.getState().logout()
  })

  afterEach(() => {
    vi.unstubAllEnvs()
    useAuthStore.getState().logout()
    Reflect.deleteProperty(navigator, 'serviceWorker')
  })

  it('does nothing when the browser has no serviceWorker support at all', () => {
    Reflect.deleteProperty(navigator, 'serviceWorker')
    vi.stubEnv('PROD', true)

    expect(() => registerServiceWorker()).not.toThrow()
  })

  it('does nothing outside a production build, even when serviceWorker support exists', () => {
    const container = installFakeServiceWorkerContainer()
    vi.stubEnv('PROD', false)

    registerServiceWorker()

    expect(container.register).not.toHaveBeenCalled()
  })

  it('registers /sw.js at the root scope in a production build', async () => {
    const container = installFakeServiceWorkerContainer()
    container.register.mockResolvedValue(new FakeRegistration())
    vi.stubEnv('PROD', true)

    registerServiceWorker()
    await flushMicrotasks()

    expect(container.register).toHaveBeenCalledWith('/sw.js')
  })

  it('flags updateAvailable once a new worker finishes installing while this page is already controlled by an older one', async () => {
    const container = installFakeServiceWorkerContainer()
    container.controller = new FakeServiceWorker('activated') // an existing controller = this is an update, not the first install
    const registration = new FakeRegistration()
    container.register.mockResolvedValue(registration)
    vi.stubEnv('PROD', true)

    registerServiceWorker()
    await flushMicrotasks()

    const newWorker = new FakeServiceWorker('installing')
    registration.triggerUpdateFound(newWorker)
    expect(useSwUpdateStore.getState().updateAvailable).toBe(false) // not yet — still installing

    newWorker.setState('installed')

    expect(useSwUpdateStore.getState().updateAvailable).toBe(true)
  })

  it('does NOT flag an update for this browser\'s very first install (no existing controller)', async () => {
    const container = installFakeServiceWorkerContainer()
    container.controller = null // nothing controlling this page yet
    const registration = new FakeRegistration()
    container.register.mockResolvedValue(registration)
    vi.stubEnv('PROD', true)

    registerServiceWorker()
    await flushMicrotasks()

    const worker = new FakeServiceWorker('installing')
    registration.triggerUpdateFound(worker)
    worker.setState('installed')

    expect(useSwUpdateStore.getState().updateAvailable).toBe(false)
  })

  it('does NOT reload on a controllerchange nobody asked for — e.g. self.clients.claim() taking over this browser\'s very first, previously-uncontrolled visit', async () => {
    // Found via a real Playwright run, not theorized: `src/sw.ts#activate`'s `self.clients.claim()`
    // fires `controllerchange` on an *uncontrolled* page gaining its first controller too, which is
    // indistinguishable at the event level from an existing controller being replaced. Reloading
    // unconditionally here means every first-time visitor gets a surprise, unrequested full page
    // reload moments after the page finishes loading.
    const container = installFakeServiceWorkerContainer()
    container.register.mockResolvedValue(new FakeRegistration())
    vi.stubEnv('PROD', true)
    const reloadSpy = vi.fn()
    Object.defineProperty(window, 'location', {
      value: { ...window.location, reload: reloadSpy },
      writable: true,
      configurable: true,
    })

    registerServiceWorker()
    await flushMicrotasks()

    container.dispatchEvent(new Event('controllerchange')) // nobody called activatePendingUpdate()

    expect(reloadSpy).not.toHaveBeenCalled()
  })

  it('reloads the page exactly once on controllerchange after the user explicitly activates an update, even if the event fires more than once', async () => {
    const container = installFakeServiceWorkerContainer()
    container.register.mockResolvedValue(new FakeRegistration())
    container.getRegistration.mockResolvedValue(new FakeRegistration())
    vi.stubEnv('PROD', true)
    const reloadSpy = vi.fn()
    Object.defineProperty(window, 'location', {
      value: { ...window.location, reload: reloadSpy },
      writable: true,
      configurable: true,
    })

    registerServiceWorker()
    await flushMicrotasks()
    activatePendingUpdate() // the user clicked "โหลดหน้าใหม่เพื่ออัปเดต"
    await flushMicrotasks()

    container.dispatchEvent(new Event('controllerchange'))
    container.dispatchEvent(new Event('controllerchange'))

    expect(reloadSpy).toHaveBeenCalledTimes(1)
  })

  it('posts CLEAR_RUNTIME_CACHE to the controller on logout (the runtime-cache sibling of the outbox\'s H-02 quarantine)', async () => {
    const container = installFakeServiceWorkerContainer()
    container.register.mockResolvedValue(new FakeRegistration())
    const postMessage = vi.fn()
    container.controller = Object.assign(new FakeServiceWorker('activated'), { postMessage })
    vi.stubEnv('PROD', true)
    useAuthStore.getState().login(OWNER_SESSION)

    registerServiceWorker()
    await flushMicrotasks()

    useAuthStore.getState().logout()

    expect(postMessage).toHaveBeenCalledWith({ type: 'CLEAR_RUNTIME_CACHE' })
  })

  it('does not post CLEAR_RUNTIME_CACHE when there was no session to begin with', async () => {
    const container = installFakeServiceWorkerContainer()
    container.register.mockResolvedValue(new FakeRegistration())
    const postMessage = vi.fn()
    container.controller = Object.assign(new FakeServiceWorker('activated'), { postMessage })
    vi.stubEnv('PROD', true)

    registerServiceWorker()
    await flushMicrotasks()
    useAuthStore.getState().logout() // logging out while already logged out — not a real transition

    expect(postMessage).not.toHaveBeenCalled()
  })
})

describe('activatePendingUpdate (S13-FE-02)', () => {
  afterEach(() => {
    useSwUpdateStore.setState({ updateAvailable: false })
    Reflect.deleteProperty(navigator, 'serviceWorker')
  })

  it('clears the update-available flag and posts SKIP_WAITING to the waiting worker', async () => {
    useSwUpdateStore.setState({ updateAvailable: true })
    const container = installFakeServiceWorkerContainer()
    const registration = new FakeRegistration()
    const postMessage = vi.fn()
    registration.waiting = Object.assign(new FakeServiceWorker('installed'), { postMessage })
    container.getRegistration.mockResolvedValue(registration)

    activatePendingUpdate()
    await flushMicrotasks()

    expect(useSwUpdateStore.getState().updateAvailable).toBe(false)
    expect(postMessage).toHaveBeenCalledWith({ type: 'SKIP_WAITING' })
  })

  it('does not throw when there is no waiting worker (nothing to activate)', async () => {
    const container = installFakeServiceWorkerContainer()
    container.getRegistration.mockResolvedValue(new FakeRegistration())

    expect(() => activatePendingUpdate()).not.toThrow()
    await flushMicrotasks()
  })
})
