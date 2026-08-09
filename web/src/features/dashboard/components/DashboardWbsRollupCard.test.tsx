import type { ComponentProps } from 'react'
import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it } from 'vitest'
import { DashboardWbsRollupCard } from './DashboardWbsRollupCard'
import type { WbsTreeNodeDto } from '../../wbs'
import type { DashboardProgressRollupDto } from '../types'

function wbsNode(overrides: Partial<WbsTreeNodeDto>): WbsTreeNodeDto {
  return {
    id: 'n1',
    parentWbsNodeId: null,
    code: 'W-01',
    title: 'โครงสร้าง',
    weightPercentage: '40.00',
    activityCount: 12,
    children: [],
    ...overrides,
  }
}

const baseRollup: DashboardProgressRollupDto = {
  progressPercentage: '54.20',
  weightWarnings: [],
  mixedScopeWbsNodeIds: [],
}

function renderCard(props: Partial<ComponentProps<typeof DashboardWbsRollupCard>> = {}) {
  return render(
    <MemoryRouter>
      <DashboardWbsRollupCard
        projectId="project-1"
        rollup={baseRollup}
        rootNodes={[]}
        loadState="ready"
        loadError={null}
        {...props}
      />
    </MemoryRouter>,
  )
}

describe('DashboardWbsRollupCard (S8-FE-01)', () => {
  it('shows the real project-wide weighted progress total from DashboardResponseDto.progressRollup', () => {
    renderCard()
    expect(screen.getByText('54.20%')).toBeInTheDocument()
  })

  it('renders the real WBS root branches (code/title/weight) from the real wbs-tree read', () => {
    const rootNodes = [
      wbsNode({ id: 'w1', code: 'W-01', title: 'โครงสร้าง', weightPercentage: '40.00' }),
      wbsNode({ id: 'w2', code: 'W-02', title: 'สถาปัตยกรรม', weightPercentage: '30.00' }),
    ]
    renderCard({ rootNodes })

    expect(screen.getByText('W-01')).toBeInTheDocument()
    expect(screen.getByText('โครงสร้าง')).toBeInTheDocument()
    expect(screen.getByText('40.00%')).toBeInTheDocument()
    expect(screen.getByText('W-02')).toBeInTheDocument()
    expect(screen.getByText('สถาปัตยกรรม')).toBeInTheDocument()
    expect(screen.getByText('30.00%')).toBeInTheDocument()
  })

  it('scope honesty: explicitly states no per-branch progress data exists, rather than fabricating plan/actual bars', () => {
    renderCard({ rootNodes: [wbsNode({})] })
    expect(screen.getByText(/ยังไม่มีข้อมูลความคืบหน้ารายหมวด/)).toBeInTheDocument()
  })

  it('surfaces a weight-warning with the real node label resolved from the wbs-tree, root-level (null id) as a distinct label', () => {
    const rootNodes = [wbsNode({ id: 'w1', code: 'W-01', title: 'โครงสร้าง' })]
    const rollup: DashboardProgressRollupDto = {
      ...baseRollup,
      weightWarnings: [
        { wbsNodeId: null, childCount: 2, weightSum: '90.00' },
        { wbsNodeId: 'w1', childCount: 3, weightSum: '80.00' },
      ],
    }

    renderCard({ rootNodes, rollup })

    const alert = screen.getByRole('alert')
    expect(alert).toHaveTextContent('ระดับบนสุดของโครงการ')
    expect(alert).toHaveTextContent('90.00%')
    expect(alert).toHaveTextContent('W-01 โครงสร้าง')
    expect(alert).toHaveTextContent('80.00%')
  })

  it('surfaces a mixed-scope node note with the real node label', () => {
    const rootNodes = [wbsNode({ id: 'w1', code: 'W-01', title: 'โครงสร้าง' })]
    const rollup: DashboardProgressRollupDto = { ...baseRollup, mixedScopeWbsNodeIds: ['w1'] }

    renderCard({ rootNodes, rollup })

    expect(screen.getByText(/W-01 โครงสร้าง/)).toBeInTheDocument()
    expect(screen.getByText(/หมวดงานย่อยเท่านั้น/)).toBeInTheDocument()
  })

  it('shows a loading state for the wbs-tree fetch', () => {
    renderCard({ loadState: 'loading' })
    expect(screen.getByRole('status')).toHaveTextContent('กำลังโหลดโครงสร้าง WBS')
  })

  it('shows a Thai error state, never a blank/broken widget', () => {
    renderCard({ loadState: 'error', loadError: 'โหลดข้อมูล WBS ไม่สำเร็จ' })
    expect(screen.getByRole('alert')).toHaveTextContent('โหลดข้อมูล WBS ไม่สำเร็จ')
  })

  it('honest empty state when the project has no WBS structure yet', () => {
    renderCard({ rootNodes: [] })
    expect(screen.getByText('ยังไม่มีโครงสร้าง WBS ในโครงการนี้')).toBeInTheDocument()
  })

  it('links through to the real WBS screen', () => {
    renderCard()
    expect(screen.getByRole('link', { name: /เปิด WBS/ })).toHaveAttribute('href', '/app/project-1/wbs')
  })
})
