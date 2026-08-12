import { describe, expect, it, vi } from 'vitest'
import { render, screen, within } from '@testing-library/react'
import { PolicyHistoryTimeline } from './PolicyHistoryTimeline'
import type { ApprovalPolicyVersionHistoryEntry } from '../types'

const entries: ApprovalPolicyVersionHistoryEntry[] = [
  {
    approvalPolicyId: 'policy-1',
    version: 1,
    isActive: false,
    effectiveFrom: '2026-01-05T03:00:00Z',
    effectiveTo: '2026-05-01T03:00:00Z',
    allowSelfApproval: true,
    cumulativeVoEscalationPct: null,
    cumulativeVoEscalationRole: null,
    ruleCount: 1,
    createdByUserId: 'user-1',
    createdAt: '2026-01-05T03:00:00Z',
    lastModifiedByUserId: null,
    lastModifiedAt: null,
  },
  {
    approvalPolicyId: 'policy-2',
    version: 2,
    isActive: true,
    effectiveFrom: '2026-05-01T03:00:00Z',
    effectiveTo: null,
    allowSelfApproval: false,
    cumulativeVoEscalationPct: '10.00',
    cumulativeVoEscalationRole: 'Executive',
    ruleCount: 2,
    createdByUserId: 'user-1',
    createdAt: '2026-05-01T03:00:00Z',
    lastModifiedByUserId: 'user-2',
    lastModifiedAt: '2026-05-01T03:00:00Z',
  },
]

const defaultProps = {
  documentType: 'VariationOrder' as const,
  entries,
  loadState: 'ready' as const,
  loadError: null,
  onRetry: vi.fn(),
}

describe('PolicyHistoryTimeline', () => {
  it('loading state', () => {
    render(<PolicyHistoryTimeline {...defaultProps} loadState="loading" entries={[]} />)
    expect(screen.getByRole('status')).toHaveTextContent('กำลังโหลดประวัติเวอร์ชันนโยบาย')
  })

  it('error state', () => {
    render(<PolicyHistoryTimeline {...defaultProps} loadState="error" entries={[]} loadError="โหลดไม่สำเร็จ" />)
    expect(screen.getByRole('alert')).toHaveTextContent('โหลดไม่สำเร็จ')
  })

  it('empty state: ready with zero entries', () => {
    render(<PolicyHistoryTimeline {...defaultProps} entries={[]} />)
    expect(screen.getByText('ยังไม่มีประวัติการเปลี่ยนแปลงนโยบายสำหรับประเภทเอกสารนี้')).toBeInTheDocument()
  })

  it('renders every version newest-first, with exactly one marked Active', () => {
    render(<PolicyHistoryTimeline {...defaultProps} />)

    const list = screen.getByRole('list', { name: 'ประวัติเวอร์ชันนโยบายตามลำดับเวลา' })
    const items = within(list).getAllByRole('listitem')
    expect(items).toHaveLength(2)
    // newest (v2) first.
    expect(within(items[0]).getByText('v2')).toBeInTheDocument()
    expect(within(items[1]).getByText('v1')).toBeInTheDocument()

    expect(within(items[0]).getByText('ใช้งานอยู่ (Active)')).toBeInTheDocument()
    expect(within(items[1]).queryByText('ใช้งานอยู่ (Active)')).not.toBeInTheDocument()
  })

  it('shows who changed each version when known, and an honest "unknown" note when not', () => {
    const noAuditEntry: ApprovalPolicyVersionHistoryEntry = {
      ...entries[0],
      approvalPolicyId: 'policy-seeded',
      version: 1,
      createdByUserId: null,
      createdAt: null,
      lastModifiedByUserId: null,
      lastModifiedAt: null,
    }
    render(<PolicyHistoryTimeline {...defaultProps} entries={[noAuditEntry, entries[1]]} />)

    expect(screen.getByText('แก้ไขโดยผู้ใช้รหัส user-2')).toBeInTheDocument()
    expect(screen.getByText(/ไม่ทราบผู้แก้ไข/)).toBeInTheDocument()
  })

  it('shows escalation config only for VariationOrder, never for PaymentCertificate', () => {
    const { rerender } = render(<PolicyHistoryTimeline {...defaultProps} documentType="VariationOrder" />)
    expect(screen.getByText(/Escalation สะสม 10\.00%/)).toBeInTheDocument()

    rerender(<PolicyHistoryTimeline {...defaultProps} documentType="PaymentCertificate" />)
    expect(screen.queryByText(/Escalation สะสม/)).not.toBeInTheDocument()
  })

  it('the refresh button calls onRetry and is disabled while loading', async () => {
    const onRetry = vi.fn()
    const { rerender } = render(<PolicyHistoryTimeline {...defaultProps} onRetry={onRetry} />)

    screen.getByRole('button', { name: 'รีเฟรช' }).click()
    expect(onRetry).toHaveBeenCalledTimes(1)

    rerender(<PolicyHistoryTimeline {...defaultProps} loadState="loading" entries={[]} onRetry={onRetry} />)
    expect(screen.getByRole('button', { name: 'รีเฟรช' })).toBeDisabled()
  })
})
