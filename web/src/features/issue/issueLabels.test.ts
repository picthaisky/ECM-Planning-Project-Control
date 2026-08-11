import { describe, expect, it } from 'vitest'
import { formatIssueCode, nextIssueActionLabel, searchIssues, toIssueRequestDate } from './issueLabels'
import type { IssueLogDto } from './types'

function makeIssue(overrides: Partial<IssueLogDto> & { id: string }): IssueLogDto {
  return {
    projectId: 'project-1',
    sequenceNo: 1,
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

describe('features/issue/issueLabels', () => {
  describe('formatIssueCode', () => {
    it('formats a sequence number with the prototype-style zero-padded prefix', () => {
      expect(formatIssueCode(24)).toBe('ISS-024')
      expect(formatIssueCode(1)).toBe('ISS-001')
      expect(formatIssueCode(1234)).toBe('ISS-1234')
    })

    it('renders null (a not-yet-reloaded mutation response) as an honest dash, never a fabricated number', () => {
      expect(formatIssueCode(null)).toBe('—')
    })
  })

  describe('nextIssueActionLabel', () => {
    // domain-rules.md §9.1: skipping Doing is not permitted — one rung per action.
    it('matches the prototype exactly: Open -> "เริ่มแก้ไข →"', () => {
      expect(nextIssueActionLabel('Open')).toBe('เริ่มแก้ไข →')
    })
    it('matches the prototype exactly: Doing -> "ปิดปัญหา ✓"', () => {
      expect(nextIssueActionLabel('Doing')).toBe('ปิดปัญหา ✓')
    })
    it('Closed has no next action — terminal, no reopen', () => {
      expect(nextIssueActionLabel('Closed')).toBeNull()
    })
  })

  describe('toIssueRequestDate', () => {
    it('converts a date-input value to a UTC-midnight ISO instant', () => {
      expect(toIssueRequestDate('2026-07-18')).toBe('2026-07-18T00:00:00.000Z')
    })
    it('an empty value becomes null, never an invalid date string', () => {
      expect(toIssueRequestDate('')).toBeNull()
    })
  })

  describe('searchIssues', () => {
    const issues = [
      makeIssue({ id: 'i1', title: 'น้ำรั่วซึมผนัง', detail: 'โซน B', owner: 'วิศวกรโครงสร้าง' }),
      makeIssue({ id: 'i2', title: 'เหล็กเส้นส่งช้า', detail: null, owner: 'จัดซื้อ' }),
    ]

    it('is a no-op for an empty/whitespace query', () => {
      expect(searchIssues(issues, '')).toEqual(issues)
      expect(searchIssues(issues, '   ')).toEqual(issues)
    })

    it('matches case-insensitively against title/detail/owner', () => {
      expect(searchIssues(issues, 'จัดซื้อ').map((i) => i.id)).toEqual(['i2'])
      expect(searchIssues(issues, 'โซน b').map((i) => i.id)).toEqual(['i1'])
    })

    it('never throws on a row with a null detail/owner', () => {
      expect(() => searchIssues(issues, 'anything')).not.toThrow()
    })
  })
})
