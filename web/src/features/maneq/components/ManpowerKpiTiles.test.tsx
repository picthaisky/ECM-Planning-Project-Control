import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import { ManpowerKpiTiles } from './ManpowerKpiTiles'
import type { SectionState } from '../useManpowerOverview'
import type { ProductivityIndexResponseDto } from '../types'

function readySection(data: Partial<ProductivityIndexResponseDto>): SectionState<ProductivityIndexResponseDto> {
  return {
    state: 'ready',
    error: null,
    data: {
      projectId: 'project-1',
      wbsNodeId: null,
      activityId: null,
      from: null,
      to: '2026-07-08T00:00:00.000Z',
      productivityIndex: null,
      productivityIndexNullReason: null,
      earnedManHours: '0.00',
      actualManHoursInScope: '0.00',
      actualManHoursTotal: '0.00',
      excludedManHours: '0.00',
      coveragePercentage: '100.00',
      logEntryCount: 0,
      warnings: [],
      manningRatio: null,
      actualWorkerCount: null,
      plannedWorkerCount: null,
      ...data,
    },
  }
}

const loadingSection: SectionState<ProductivityIndexResponseDto> = { state: 'loading', error: null, data: null }

describe('ManpowerKpiTiles', () => {
  it('fixture M-02: shows productivityIndex 0.60 (red/danger) while manningRatio implies 1.25 — never lets the manning delta read as "good"', () => {
    render(
      <ManpowerKpiTiles
        today={readySection({ actualWorkerCount: 25, plannedWorkerCount: 20, manningRatio: '1.25' })}
        monthToDate={loadingSection}
        cumulativePi={readySection({ productivityIndex: '0.60' })}
      />,
    )

    // The PI tile shows the real (low) PI value — never the manning ratio's number.
    expect(screen.getByText('0.60')).toBeInTheDocument()
    // The manning tile shows the real headcount and delta, but its tone must never claim "success" —
    // asserted via the tone-bearing element's class list not containing the success/green token.
    const manningValue = screen.getByText('25 คน')
    expect(manningValue.className).not.toMatch(/text-success/)
  })

  it('renders "—" with the specific Thai reason (never a bare dash) when PI is null for NoBudgetManHours', () => {
    render(
      <ManpowerKpiTiles
        today={loadingSection}
        monthToDate={loadingSection}
        cumulativePi={readySection({ productivityIndex: null, productivityIndexNullReason: 'NoBudgetManHours' })}
      />,
    )

    // Two tiles legitimately show "—" here (the always-"—" equipment tile and this null PI tile) —
    // the load-bearing assertion is the *reason* text, per this task's explicit instruction that a
    // bare dash alone is not acceptable.
    expect(screen.getAllByText('—').length).toBeGreaterThanOrEqual(1)
    expect(screen.getByText(/ยังไม่ได้ประมาณการเป็นชั่วโมง-คน/)).toBeInTheDocument()
  })

  it('renders a defined 0.00 (not "—") when PI is a real zero (fixture M-06c)', () => {
    render(
      <ManpowerKpiTiles
        today={loadingSection}
        monthToDate={loadingSection}
        cumulativePi={readySection({ productivityIndex: '0.00', productivityIndexNullReason: null })}
      />,
    )

    // "0.00" is a defined, real value distinct from the null "—" case — asserting its presence (not
    // an absence-of-dash check, which would also trip over the equipment tile's own unconditional
    // "—") is the correct way to pin ADR-0013(f)'s null-vs-zero discipline here.
    expect(screen.getByText('0.00')).toBeInTheDocument()
  })

  it('shows a coverage note when coverage is below 100%', () => {
    render(
      <ManpowerKpiTiles
        today={loadingSection}
        monthToDate={loadingSection}
        cumulativePi={readySection({ productivityIndex: '0.80', coveragePercentage: '71.43' })}
      />,
    )
    expect(screen.getByText(/ครอบคลุมข้อมูล 71.43%/)).toBeInTheDocument()
  })

  it('renders the equipment tile as "—" with an honest gap explanation, never a fabricated figure', () => {
    render(<ManpowerKpiTiles today={loadingSection} monthToDate={loadingSection} cumulativePi={loadingSection} />)
    expect(screen.getByText('เครื่องจักรทำงาน')).toBeInTheDocument()
    expect(screen.getByText(/ยังไม่มี endpoint/)).toBeInTheDocument()
  })

  it('shows the month-to-date hours tile with a hours unit suffix', () => {
    render(
      <ManpowerKpiTiles
        today={loadingSection}
        monthToDate={readySection({ actualManHoursTotal: '38420.00' })}
        cumulativePi={loadingSection}
      />,
    )
    expect(screen.getByText('38,420.00 ชม.')).toBeInTheDocument()
  })

  it("shows '—' for the manning delta caption when no plan is configured for today (never fabricates a shortfall)", () => {
    render(
      <ManpowerKpiTiles
        today={readySection({ actualWorkerCount: 88, plannedWorkerCount: null })}
        monthToDate={loadingSection}
        cumulativePi={loadingSection}
      />,
    )
    expect(screen.getByText('ยังไม่มีแผนกำลังคนสำหรับวันนี้ — ไม่แสดงส่วนต่าง')).toBeInTheDocument()
  })
})
