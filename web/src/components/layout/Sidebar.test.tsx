import 'fake-indexeddb/auto'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { Sidebar } from './Sidebar'
import { NAV_ENTRIES } from './navConfig'
import { useAuthStore } from '../../store/authStore'
import { createIndexedDbOutboxStorage, createOutboxStore } from '../../services/outbox'

function renderAt(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/app/:projectId/*" element={<Sidebar />} />
      </Routes>
    </MemoryRouter>,
  )
}

/** S13-FE-03: `Sidebar` now reads the real (fake-indexeddb-polyfilled) `cmplus-outbox` database via
 * `useOutboxSyncStatus` — mirrors `features/photo/usePhotoOutbox.test.ts`'s identical reset helper. */
function resetOutboxDatabase(): Promise<void> {
  return new Promise((resolve) => {
    const request = indexedDB.deleteDatabase('cmplus-outbox')
    request.onsuccess = () => resolve()
    request.onerror = () => resolve()
    request.onblocked = () => resolve()
  })
}

const OWNER_SESSION = {
  accessToken: 'jwt',
  expiresAt: '2027-01-01T00:00:00+07:00',
  userId: 'user-1',
  tenantId: 'tenant-1',
  role: 'PM' as const,
}

describe('Sidebar (S4-FE-01, ADR-0006 visual parity)', () => {
  beforeEach(async () => {
    await resetOutboxDatabase()
    useAuthStore.getState().logout()
  })

  afterEach(async () => {
    useAuthStore.getState().logout()
    await resetOutboxDatabase()
  })

  it('renders the exact 13-screen nav, in the prototype-transcribed order', () => {
    renderAt('/app/project-1/dashboard')

    const links = screen.getAllByRole('link')
    expect(links).toHaveLength(13)
    expect(links.map((l) => l.textContent)).toEqual(NAV_ENTRIES.map((e) => e.label))
  })

  it('the current screen gets the gold active-state background; every other item does not', () => {
    renderAt('/app/project-1/wbs')

    const wbsLink = screen.getByRole('link', { name: 'WBS & Activity' })
    const infoLink = screen.getByRole('link', { name: 'ข้อมูลโครงการ' })

    expect(wbsLink.className).toContain('bg-gold')
    expect(wbsLink.className).toContain('text-navy')
    expect(infoLink.className).not.toContain('bg-gold')
    expect(infoLink.className).toContain('text-white/75')
  })

  it('links route under /app/:projectId/<screen>, preserving the current project id', () => {
    renderAt('/app/project-42/wbs')

    expect(screen.getByRole('link', { name: 'Payment Certificate' })).toHaveAttribute(
      'href',
      '/app/project-42/payment',
    )
  })

  it('shows the logged-in user role and logs out on click', async () => {
    useAuthStore.getState().login({
      accessToken: 'jwt',
      expiresAt: '2027-01-01T00:00:00+07:00',
      userId: 'user-1',
      tenantId: 'tenant-1',
      role: 'PM',
    })

    renderAt('/app/project-1/dashboard')
    expect(screen.getByText('Project Manager')).toBeInTheDocument()

    screen.getByRole('button', { name: 'ออกจากระบบ' }).click()
    expect(useAuthStore.getState().isAuthenticated).toBe(false)
  })

  describe('S13-FE-03: N-03 sign-out gate (Sprint 12 security review)', () => {
    it('with nothing pending, "ออกจากระบบ" still logs out immediately — no gate for the common case', async () => {
      useAuthStore.getState().login(OWNER_SESSION)
      renderAt('/app/project-1/dashboard')

      await userEvent.click(screen.getByRole('button', { name: 'ออกจากระบบ' }))

      expect(useAuthStore.getState().isAuthenticated).toBe(false)
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    })

    it('with an un-synced item queued, "ออกจากระบบ" opens a confirmation instead of logging out immediately', async () => {
      useAuthStore.getState().login(OWNER_SESSION)
      const store = createOutboxStore({ storage: createIndexedDbOutboxStorage(), getOwner: () => ({ userId: OWNER_SESSION.userId, tenantId: OWNER_SESSION.tenantId }) })
      await store.enqueue({ kind: 'photo', payload: {}, blob: new Blob(['x'], { type: 'image/jpeg' }) })

      renderAt('/app/project-1/dashboard')
      await waitFor(() => expect(screen.getByText(/รอซิงค์ 1 รายการ/)).toBeInTheDocument())

      await userEvent.click(screen.getByRole('button', { name: 'ออกจากระบบ' }))

      expect(screen.getByRole('dialog')).toBeInTheDocument()
      expect(screen.getByText(/คุณมีรายการที่ยังไม่ได้ซิงค์ 1 รายการ/)).toBeInTheDocument()
      // The blob-loss warning is specific to the photo kind actually queued here.
      expect(screen.getByText(/ไฟล์แนบ.*จะถูกลบออกจากอุปกรณ์นี้เพื่อความปลอดภัย/)).toBeInTheDocument()
      expect(useAuthStore.getState().isAuthenticated).toBe(true) // not logged out yet
    })

    it('"ยกเลิก" closes the gate without logging out', async () => {
      useAuthStore.getState().login(OWNER_SESSION)
      const store = createOutboxStore({ storage: createIndexedDbOutboxStorage(), getOwner: () => ({ userId: OWNER_SESSION.userId, tenantId: OWNER_SESSION.tenantId }) })
      await store.enqueue({ kind: 'weather-log', payload: {} })

      renderAt('/app/project-1/dashboard')
      await waitFor(() => expect(screen.getByText(/รอซิงค์ 1 รายการ/)).toBeInTheDocument())
      await userEvent.click(screen.getByRole('button', { name: 'ออกจากระบบ' }))

      await userEvent.click(screen.getByRole('button', { name: 'ยกเลิก' }))

      expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
      expect(useAuthStore.getState().isAuthenticated).toBe(true)
    })

    it('"ออกจากระบบโดยไม่ซิงค์" proceeds with logout despite the pending item', async () => {
      useAuthStore.getState().login(OWNER_SESSION)
      const store = createOutboxStore({ storage: createIndexedDbOutboxStorage(), getOwner: () => ({ userId: OWNER_SESSION.userId, tenantId: OWNER_SESSION.tenantId }) })
      await store.enqueue({ kind: 'photo', payload: {}, blob: new Blob(['x'], { type: 'image/jpeg' }) })

      renderAt('/app/project-1/dashboard')
      await waitFor(() => expect(screen.getByText(/รอซิงค์ 1 รายการ/)).toBeInTheDocument())
      await userEvent.click(screen.getByRole('button', { name: 'ออกจากระบบ' }))

      await userEvent.click(screen.getByRole('button', { name: 'ออกจากระบบโดยไม่ซิงค์' }))

      expect(useAuthStore.getState().isAuthenticated).toBe(false)
    })
  })
})
