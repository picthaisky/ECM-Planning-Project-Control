import { afterAll, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { IssuePage } from './IssuePage'
import * as issueApi from './api'
import { useAuthStore } from '../../store/authStore'
import type { UserRole } from '../../store/authStore'
import type { IssueListResultDto, IssueLogDto } from './types'

beforeAll(() => {
  Object.defineProperty(HTMLElement.prototype, 'offsetHeight', { configurable: true, get: () => 440 })
  Object.defineProperty(HTMLElement.prototype, 'offsetWidth', { configurable: true, get: () => 900 })
})

afterAll(() => {
  Reflect.deleteProperty(HTMLElement.prototype, 'offsetHeight')
  Reflect.deleteProperty(HTMLElement.prototype, 'offsetWidth')
})

vi.mock('./api', async () => {
  const actual = await vi.importActual<typeof import('./api')>('./api')
  return { ...actual, listIssues: vi.fn(), createIssue: vi.fn(), advanceIssueStatus: vi.fn() }
})

const openIssue: IssueLogDto = {
  id: 'issue-1',
  projectId: 'project-1',
  sequenceNo: 24,
  title: 'น้ำรั่วซึมผนัง Basement โซน B',
  detail: 'พบคราบน้ำหลังฝนตกหนัก 8 ก.ค.',
  owner: 'วิศวกรโครงสร้าง',
  dueDate: '2026-07-18T00:00:00+07:00',
  status: 'Open',
  startedAt: null,
  closedAt: null,
  createdByUserId: 'user-1',
  createdAt: '2026-07-08T00:00:00+07:00',
}

const doingIssue: IssueLogDto = {
  ...openIssue,
  id: 'issue-2',
  sequenceNo: 23,
  title: 'เหล็กเส้นส่งช้า',
  status: 'Doing',
}

const listResult: IssueListResultDto = {
  items: [openIssue, doingIssue],
  totalCount: 2,
  statusCounts: { open: 1, doing: 1, closed: 0 },
}

function renderPage(role: UserRole = 'PM') {
  useAuthStore.getState().login({
    accessToken: 'jwt',
    expiresAt: '2027-01-01T00:00:00+07:00',
    userId: 'user-1',
    tenantId: 'tenant-1',
    role,
  })

  return render(
    <MemoryRouter initialEntries={['/app/project-1/issue']}>
      <Routes>
        <Route path="/app/:projectId/issue" element={<IssuePage />} />
      </Routes>
    </MemoryRouter>,
  )
}

describe('IssuePage', () => {
  beforeEach(() => {
    useAuthStore.getState().logout()
    vi.mocked(issueApi.listIssues).mockReset()
    vi.mocked(issueApi.createIssue).mockReset()
    vi.mocked(issueApi.advanceIssueStatus).mockReset()
  })

  it('loads the register and the tiles reflect the server statusCounts, not a client recount', async () => {
    vi.mocked(issueApi.listIssues).mockResolvedValueOnce(listResult)
    renderPage()

    await waitFor(() => expect(screen.getByText('น้ำรั่วซึมผนัง Basement โซน B')).toBeInTheDocument())
    expect(screen.getByText('ทั้งหมด', { selector: 'p' }).nextSibling).toHaveTextContent('2')
    expect(screen.getByText('เปิดอยู่ (Open)').nextSibling).toHaveTextContent('1')
    expect(screen.getByText('กำลังแก้ไข (Doing)').nextSibling).toHaveTextContent('1')
    expect(screen.getByText('ปิดแล้ว (Closed)').nextSibling).toHaveTextContent('0')
  })

  it('advancing status reloads the whole list so the tiles and table stay atomically consistent', async () => {
    vi.mocked(issueApi.listIssues).mockResolvedValue(listResult)
    vi.mocked(issueApi.advanceIssueStatus).mockResolvedValueOnce({ ...openIssue, status: 'Doing' })
    renderPage()

    await waitFor(() => expect(screen.getByRole('button', { name: 'เริ่มแก้ไข →' })).toBeInTheDocument())
    await userEvent.click(screen.getByRole('button', { name: 'เริ่มแก้ไข →' }))

    await waitFor(() => expect(issueApi.listIssues).toHaveBeenCalledTimes(2))
  })

  it('a write-role user can open the create modal and submit a new issue', async () => {
    vi.mocked(issueApi.listIssues).mockResolvedValue({ items: [], totalCount: 0, statusCounts: { open: 0, doing: 0, closed: 0 } })
    vi.mocked(issueApi.createIssue).mockResolvedValueOnce({ ...openIssue, sequenceNo: null })
    renderPage('Site')

    await waitFor(() => expect(screen.getByRole('button', { name: '+ แจ้งปัญหาใหม่' })).toBeInTheDocument())
    await userEvent.click(screen.getByRole('button', { name: '+ แจ้งปัญหาใหม่' }))
    await userEvent.type(screen.getByLabelText(/หัวข้อปัญหา/), 'ปัญหาทดสอบ')
    await userEvent.click(screen.getByRole('button', { name: 'บันทึก' }))

    await waitFor(() => expect(issueApi.createIssue).toHaveBeenCalled())
  })

  it('a non-write-role user never sees "+ แจ้งปัญหาใหม่" or the advance action', async () => {
    vi.mocked(issueApi.listIssues).mockResolvedValueOnce(listResult)
    renderPage('Executive')

    await waitFor(() => expect(screen.getByText('น้ำรั่วซึมผนัง Basement โซน B')).toBeInTheDocument())
    expect(screen.queryByRole('button', { name: '+ แจ้งปัญหาใหม่' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'เริ่มแก้ไข →' })).not.toBeInTheDocument()
  })

  it('the status filter tabs narrow the table client-side without changing the tiles', async () => {
    vi.mocked(issueApi.listIssues).mockResolvedValueOnce(listResult)
    renderPage()

    await waitFor(() => expect(screen.getByRole('table')).toHaveAttribute('aria-rowcount', '2'))

    await userEvent.click(screen.getByRole('button', { name: 'เปิดอยู่' }))
    expect(screen.getByRole('table')).toHaveAttribute('aria-rowcount', '1')
    // Tiles are unaffected by the client-side table filter — still the server's real totals.
    expect(screen.getByText('ทั้งหมด', { selector: 'p' }).nextSibling).toHaveTextContent('2')
  })

  it('the search box narrows the table client-side', async () => {
    vi.mocked(issueApi.listIssues).mockResolvedValueOnce(listResult)
    renderPage()

    await waitFor(() => expect(screen.getByRole('table')).toHaveAttribute('aria-rowcount', '2'))
    await userEvent.type(screen.getByPlaceholderText(/ค้นหาหัวข้อ/), 'เหล็กเส้น')
    expect(screen.getByRole('table')).toHaveAttribute('aria-rowcount', '1')
  })
})
