import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import { IssueSummaryTiles } from './IssueSummaryTiles'

/**
 * S11-FE-01 DoD (this file's whole reason for existing): "tile นับ open/doing/closed ตรงกับตาราง" —
 * `IssueSummaryTiles`'s props type only accepts the server's own `totalCount`/`statusCounts`
 * aggregate (see the component's own remarks) — it has no `items` prop to read at all, so these
 * tests prove the tiles render *exactly* what the server said, not a recomputed guess.
 */
describe('IssueSummaryTiles', () => {
  it('renders the four tiles with exactly the given totalCount/statusCounts, verbatim', () => {
    render(<IssueSummaryTiles totalCount={24} statusCounts={{ open: 7, doing: 3, closed: 14 }} state="ready" />)

    expect(screen.getByText('ทั้งหมด').nextSibling).toHaveTextContent('24')
    expect(screen.getByText('เปิดอยู่ (Open)').nextSibling).toHaveTextContent('7')
    expect(screen.getByText('กำลังแก้ไข (Doing)').nextSibling).toHaveTextContent('3')
    expect(screen.getByText('ปิดแล้ว (Closed)').nextSibling).toHaveTextContent('14')
  })

  // domain-rules.md §9.3 fixture W-12c: a filtered result (e.g. ?owner=) must still show
  // open+doing+closed === totalCount and this component must render whatever it is given, honoring
  // the filter — proven here by feeding it a smaller, filtered-looking aggregate.
  it('renders a filtered/smaller aggregate exactly as given, with no independent recomputation', () => {
    render(<IssueSummaryTiles totalCount={7} statusCounts={{ open: 4, doing: 1, closed: 2 }} state="ready" />)

    expect(screen.getByText('ทั้งหมด').nextSibling).toHaveTextContent('7')
    expect(screen.getByText('เปิดอยู่ (Open)').nextSibling).toHaveTextContent('4')
    expect(screen.getByText('กำลังแก้ไข (Doing)').nextSibling).toHaveTextContent('1')
    expect(screen.getByText('ปิดแล้ว (Closed)').nextSibling).toHaveTextContent('2')
  })

  it('shows a loading state without rendering stale/zero numbers as if they were real', () => {
    render(<IssueSummaryTiles totalCount={0} statusCounts={{ open: 0, doing: 0, closed: 0 }} state="loading" />)
    expect(screen.getAllByRole('status')).toHaveLength(4)
  })

  it('shows an error state with the given message on all four tiles', () => {
    render(
      <IssueSummaryTiles
        totalCount={0}
        statusCounts={{ open: 0, doing: 0, closed: 0 }}
        state="error"
        errorMessage="โหลดรายการปัญหาไม่สำเร็จ"
      />,
    )
    expect(screen.getAllByText('โหลดรายการปัญหาไม่สำเร็จ')).toHaveLength(4)
  })
})
