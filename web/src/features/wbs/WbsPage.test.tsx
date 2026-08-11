import 'fake-indexeddb/auto'
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { WbsPage } from './WbsPage'
import * as api from './api'
import { useAuthStore } from '../../store/authStore'
import type { ActivityForProgress, WbsTreeDto } from './types'

vi.mock('./api', async () => {
  const actual = await vi.importActual<typeof import('./api')>('./api')
  return {
    ...actual,
    getWbsTreeWithCacheInfo: vi.fn(),
    getNodeActivities: vi.fn(),
    batchRecordProgress: vi.fn(),
    recalculateCpm: vi.fn(),
  }
})

/** S13-FE-01: the batch-progress submit now enqueues into the real (fake-indexeddb-polyfilled)
 * `cmplus-outbox` database before syncing — mirrors `features/photo/usePhotoOutbox.test.ts`'s
 * identical reset helper, needed to isolate each test's outbox state under the fixed production
 * database name. */
function resetOutboxDatabase(): Promise<void> {
  return new Promise((resolve) => {
    const request = indexedDB.deleteDatabase('cmplus-outbox')
    request.onsuccess = () => resolve()
    request.onerror = () => resolve()
    request.onblocked = () => resolve()
  })
}

beforeAll(() => {
  Object.defineProperty(HTMLElement.prototype, 'offsetHeight', { configurable: true, get: () => 520 })
  Object.defineProperty(HTMLElement.prototype, 'offsetWidth', { configurable: true, get: () => 900 })
})

afterAll(() => {
  Reflect.deleteProperty(HTMLElement.prototype, 'offsetHeight')
  Reflect.deleteProperty(HTMLElement.prototype, 'offsetWidth')
})

const sampleTree: WbsTreeDto = {
  projectId: 'project-1',
  rootNodes: [
    {
      id: 'root-1',
      parentWbsNodeId: null,
      code: '1',
      title: 'งานโครงสร้าง',
      weightPercentage: '38.00',
      activityCount: 4,
      children: [],
    },
  ],
}

const nodeActivities: ActivityForProgress[] = [
  { id: '11111111-1111-1111-1111-111111111111', activityCode: 'ACT-100', name: 'เทคอนกรีต', currentProgressPercentage: '40.00' },
]

function renderPage(role: 'PM' | 'Executive') {
  useAuthStore.getState().login({
    accessToken: 'jwt',
    expiresAt: '2027-01-01T00:00:00+07:00',
    userId: 'user-1',
    tenantId: 'tenant-1',
    role,
  })

  return render(
    <MemoryRouter initialEntries={['/app/project-1/wbs']}>
      <Routes>
        <Route path="/app/:projectId/wbs" element={<WbsPage />} />
      </Routes>
    </MemoryRouter>,
  )
}

describe('WbsPage (S4-FE-03 integration)', () => {
  beforeEach(async () => {
    await resetOutboxDatabase()
    vi.mocked(api.getWbsTreeWithCacheInfo).mockReset()
    vi.mocked(api.getNodeActivities).mockReset()
    vi.mocked(api.batchRecordProgress).mockReset()
    vi.mocked(api.recalculateCpm).mockReset()
    useAuthStore.getState().logout()
  })

  afterEach(async () => {
    useAuthStore.getState().logout()
    await resetOutboxDatabase()
  })

  it('an allowed role (PM) sees the update-progress toggle; a disallowed role (Executive) does not', async () => {
    vi.mocked(api.getWbsTreeWithCacheInfo).mockResolvedValue({ tree: sampleTree, servedFromOfflineCache: false })

    renderPage('Executive')
    await waitFor(() => expect(screen.getByText('งานโครงสร้าง')).toBeInTheDocument())
    expect(screen.queryByRole('button', { name: 'โหมดอัปเดตความคืบหน้า' })).not.toBeInTheDocument()
    // S5-FE-01: same role gate as CpmController's own [Authorize(Roles = "PM,Planning,Admin")] —
    // Executive can view the WBS tree but must not see the recalculate action at all.
    expect(screen.queryByRole('button', { name: 'คำนวณ CPM ใหม่' })).not.toBeInTheDocument()
  })

  it('S5-FE-01: an allowed role (PM) can trigger a real CPM recalculation and sees the critical path preview in schedule order', async () => {
    vi.mocked(api.getWbsTreeWithCacheInfo).mockResolvedValue({ tree: sampleTree, servedFromOfflineCache: false })
    vi.mocked(api.recalculateCpm).mockResolvedValueOnce({
      activitiesProcessed: 4,
      criticalActivityCount: 3,
      projectDurationDays: 15,
      criticalPath: ['activity-a', 'activity-c', 'activity-d'],
    })
    const user = userEvent.setup()

    renderPage('PM')
    await waitFor(() => expect(screen.getByText('งานโครงสร้าง')).toBeInTheDocument())

    const recalcButton = screen.getByRole('button', { name: 'คำนวณ CPM ใหม่' })
    await user.click(recalcButton)

    expect(api.recalculateCpm).toHaveBeenCalledWith('project-1')
    await waitFor(() => expect(screen.getByText('คำนวณสำเร็จ')).toBeInTheDocument())

    const listItems = screen.getAllByRole('listitem')
    expect(listItems.map((li) => li.textContent)).toEqual([
      expect.stringContaining('activity-a'),
      expect.stringContaining('activity-c'),
      expect.stringContaining('activity-d'),
    ])
  })

  it('S5-FE-01: a rejected recalculation (e.g. a detected relation cycle) shows a clear failure state, not a silent no-op', async () => {
    vi.mocked(api.getWbsTreeWithCacheInfo).mockResolvedValue({ tree: sampleTree, servedFromOfflineCache: false })
    vi.mocked(api.recalculateCpm).mockRejectedValueOnce(
      new api.WbsApiError(
        'พบการอ้างอิงกิจกรรมแบบวนซ้ำ (Cycle) ไม่สามารถคำนวณตารางเวลาได้ (เส้นทางวนซ้ำ: A → B → A)',
        422,
      ),
    )
    const user = userEvent.setup()

    renderPage('PM')
    await waitFor(() => expect(screen.getByText('งานโครงสร้าง')).toBeInTheDocument())
    await user.click(screen.getByRole('button', { name: 'คำนวณ CPM ใหม่' }))

    await waitFor(() => expect(screen.getByText('คำนวณไม่สำเร็จ')).toBeInTheDocument())
    expect(screen.getByRole('alert')).toHaveTextContent('วนซ้ำ')
  })

  it('full flow: enter update-progress mode, pick a node\'s activities, edit a value below current, confirm the decrease, and submit the real batch call', async () => {
    vi.mocked(api.getWbsTreeWithCacheInfo).mockResolvedValue({ tree: sampleTree, servedFromOfflineCache: false })
    vi.mocked(api.getNodeActivities).mockResolvedValueOnce(nodeActivities)
    vi.mocked(api.batchRecordProgress).mockResolvedValueOnce({ entriesRecorded: 1 })
    const user = userEvent.setup()

    renderPage('PM')
    await waitFor(() => expect(screen.getByText('งานโครงสร้าง')).toBeInTheDocument())

    await user.click(screen.getByRole('button', { name: 'โหมดอัปเดตความคืบหน้า' }))
    await user.click(screen.getByText('+ เพิ่มกิจกรรม'))

    await waitFor(() => expect(screen.getByText('ACT-100')).toBeInTheDocument())
    expect(api.getNodeActivities).toHaveBeenCalledWith('project-1', 'root-1')

    const periodInput = screen.getByLabelText('วันที่ของงวดข้อมูล (Period End Date)')
    await user.type(periodInput, '2026-07-27')

    const progressInput = screen.getByLabelText(/% ความคืบหน้าใหม่ของ ACT-100/)
    await user.clear(progressInput)
    await user.type(progressInput, '20')

    await user.click(screen.getByRole('button', { name: 'ส่งข้อมูลความคืบหน้า' }))
    expect(await screen.findByRole('heading', { name: 'ยืนยันการปรับลดความคืบหน้า' })).toBeInTheDocument()
    expect(api.batchRecordProgress).not.toHaveBeenCalled()

    await user.click(screen.getByRole('button', { name: 'ยืนยันการปรับลด' }))

    await waitFor(() =>
      expect(api.batchRecordProgress).toHaveBeenCalledWith(
        'project-1',
        {
          periodEndDate: '2026-07-27T00:00:00.000Z',
          entries: [{ activityId: nodeActivities[0].id, progressPercentage: '20', actualQuantity: null }],
        },
        expect.any(String),
      ),
    )
  })

  it('the manual "+ เพิ่ม" add-row path works even when getNodeActivities 404s (no list endpoint yet)', async () => {
    vi.mocked(api.getWbsTreeWithCacheInfo).mockResolvedValue({ tree: sampleTree, servedFromOfflineCache: false })
    vi.mocked(api.getNodeActivities).mockRejectedValueOnce(new api.WbsApiError('ดำเนินการไม่สำเร็จ กรุณาลองใหม่อีกครั้ง', 404))
    const user = userEvent.setup()

    renderPage('PM')
    await waitFor(() => expect(screen.getByText('งานโครงสร้าง')).toBeInTheDocument())
    await user.click(screen.getByRole('button', { name: 'โหมดอัปเดตความคืบหน้า' }))
    await user.click(screen.getByText('+ เพิ่มกิจกรรม'))

    await waitFor(() => expect(screen.getByText(/ยังสามารถเพิ่มกิจกรรมด้วยรหัส/)).toBeInTheDocument())

    await user.type(
      screen.getByLabelText('เพิ่มกิจกรรมด้วยรหัส (Activity ID)'),
      '33333333-3333-3333-3333-333333333333',
    )
    await user.click(screen.getByRole('button', { name: '+ เพิ่ม' }))

    expect(screen.getByText('33333333-3333-3333-3333-333333333333')).toBeInTheDocument()
  })
})
