import { afterAll, beforeAll, describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { IssueTable } from './IssueTable'
import type { IssueLogDto } from '../types'

beforeAll(() => {
  Object.defineProperty(HTMLElement.prototype, 'offsetHeight', { configurable: true, get: () => 440 })
  Object.defineProperty(HTMLElement.prototype, 'offsetWidth', { configurable: true, get: () => 900 })
})

afterAll(() => {
  Reflect.deleteProperty(HTMLElement.prototype, 'offsetHeight')
  Reflect.deleteProperty(HTMLElement.prototype, 'offsetWidth')
})

function makeIssue(overrides: Partial<IssueLogDto> & { id: string }): IssueLogDto {
  return {
    projectId: 'project-1',
    sequenceNo: 24,
    title: 'น้ำรั่วซึมผนัง Basement โซน B',
    detail: 'พบคราบน้ำหลังฝนตกหนัก 8 ก.ค.',
    owner: 'วิศวกรโครงสร้าง',
    dueDate: '2026-07-18T00:00:00Z',
    status: 'Open',
    startedAt: null,
    closedAt: null,
    createdByUserId: 'user-1',
    createdAt: '2026-07-08T00:00:00Z',
    ...overrides,
  }
}

describe('IssueTable', () => {
  it('an Open issue shows "เริ่มแก้ไข →"', () => {
    render(<IssueTable items={[makeIssue({ id: 'i1', status: 'Open' })]} state="ready" canWrite advancingId={null} onAdvance={vi.fn()} />)
    expect(screen.getByRole('button', { name: 'เริ่มแก้ไข →' })).toBeInTheDocument()
  })

  it('a Doing issue shows "ปิดปัญหา ✓" — never a direct Open->Closed shortcut', () => {
    render(<IssueTable items={[makeIssue({ id: 'i1', status: 'Doing' })]} state="ready" canWrite advancingId={null} onAdvance={vi.fn()} />)
    expect(screen.getByRole('button', { name: 'ปิดปัญหา ✓' })).toBeInTheDocument()
  })

  it('a Closed issue shows "ปิดเมื่อ {date}" with no action button (terminal, no reopen)', () => {
    render(
      <IssueTable
        items={[makeIssue({ id: 'i1', status: 'Closed', closedAt: '2026-07-14T08:30:00Z' })]}
        state="ready"
        canWrite
        advancingId={null}
        onAdvance={vi.fn()}
      />,
    )
    expect(screen.queryByRole('button', { name: /เริ่มแก้ไข|ปิดปัญหา/ })).not.toBeInTheDocument()
    expect(screen.getByText(/ปิดเมื่อ/)).toBeInTheDocument()
  })

  it('hides the action entirely when canWrite is false', () => {
    render(<IssueTable items={[makeIssue({ id: 'i1', status: 'Open' })]} state="ready" canWrite={false} advancingId={null} onAdvance={vi.fn()} />)
    expect(screen.queryByRole('button', { name: 'เริ่มแก้ไข →' })).not.toBeInTheDocument()
  })

  it('calls onAdvance with the row when clicked', async () => {
    const issue = makeIssue({ id: 'i1', status: 'Open' })
    const onAdvance = vi.fn()
    render(<IssueTable items={[issue]} state="ready" canWrite advancingId={null} onAdvance={onAdvance} />)

    await userEvent.click(screen.getByRole('button', { name: 'เริ่มแก้ไข →' }))
    expect(onAdvance).toHaveBeenCalledWith(issue)
  })

  it('shows the formatted "ISS-024" code and an honest dash for a null sequenceNo', () => {
    render(
      <IssueTable
        items={[makeIssue({ id: 'i1', sequenceNo: 24 }), makeIssue({ id: 'i2', sequenceNo: null })]}
        state="ready"
        canWrite={false}
        advancingId={null}
        onAdvance={vi.fn()}
      />,
    )
    expect(screen.getByText('ISS-024')).toBeInTheDocument()
    expect(screen.getByText('—')).toBeInTheDocument()
  })
})
