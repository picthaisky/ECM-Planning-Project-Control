import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { EotEvaluationPanel } from './EotEvaluationPanel'
import type { useEotEvaluation } from '../useEotEvaluation'
import type { EotEvaluationDto } from '../types'

/** Same "hand-built hook-shaped prop" pattern as `features/wbs/components/CriticalPathPreview.test.tsx`. */
type Eot = ReturnType<typeof useEotEvaluation>

function makeEot(overrides: Partial<Eot> = {}): Eot {
  return {
    state: 'idle',
    error: null,
    result: null,
    evaluate: vi.fn().mockResolvedValue(null),
    ...overrides,
  }
}

const substantiated: EotEvaluationDto = {
  id: 'eval-1',
  projectId: 'project-1',
  windowStart: '2026-07-01T00:00:00+07:00',
  windowEnd: '2026-07-31T00:00:00+07:00',
  evaluatedAt: '2026-08-01T10:00:00+07:00',
  evaluatedByUserId: 'user-1',
  criticalityBasis: 'Contemporaneous',
  confidence: 'Substantiated',
  asScheduledDurationDays: 15,
  impactedDurationDays: 17,
  eotEligibleDays: 2,
  countableStoppageDayCount: 5,
  distinctCountableDateCount: 5,
  unattributedStoppageDayCount: 1,
  concurrencyAssessed: false,
  entitlementBasisAssessed: false,
  latestNoticeDate: null,
  noticeWindowExpired: null,
  runs: [{ cpmRunId: 'run-1', windowFrom: '2026-07-01T00:00:00+07:00', windowTo: '2026-07-31T00:00:00+07:00', asScheduledDurationDays: 15, impactedDurationDays: 17, deltaDays: 2 }],
  sources: [
    { dailyWeatherLogId: 'log-1', countableDays: '1.00', exclusionReason: null },
    { dailyWeatherLogId: 'log-2', countableDays: '0.00', exclusionReason: 'NonWorkingDay' },
  ],
  drivers: [
    {
      cpmRunId: 'run-1',
      activityId: 'activity-b',
      activityCode: 'B',
      activityName: 'งานโครงสร้าง B',
      stoppageDays: 5,
      totalFloatAtRun: 3,
      wasCriticalAtRun: false,
      isOnImpactedCriticalPath: true,
      indicativeEotDays: 2,
      marginalEotDays: 2,
      remainingFloatAfter: 0,
      unclaimedFractionalHours: null,
    },
  ],
}

describe('EotEvaluationPanel', () => {
  it('ADR-0020: the headline is the relabelled schedule-impact wording, never "สิทธิ์ขยายสัญญา"', () => {
    render(<EotEvaluationPanel eot={makeEot({ state: 'success', result: substantiated })} canEvaluate />)
    expect(screen.getByText('ผลกระทบต่อกำหนดแล้วเสร็จ (EOT ที่ประเมินได้)')).toBeInTheDocument()
    expect(screen.queryByText(/สิทธิ์ขยายสัญญา/)).not.toBeInTheDocument()
  })

  it('renders the §2.2 disclosure verbatim once a result exists', () => {
    render(<EotEvaluationPanel eot={makeEot({ state: 'success', result: substantiated })} canEvaluate />)
    expect(
      screen.getByText('ตัวเลขนี้คือผลกระทบต่อกำหนดแล้วเสร็จตามตารางงาน ไม่ใช่สิทธิ์ตามสัญญา'),
    ).toBeInTheDocument()
    expect(
      screen.getByText(
        /การได้รับสิทธิ์ขยายเวลาขึ้นกับเงื่อนไขสัญญา.*และการพิจารณาความล่าช้าที่เกิดพร้อมกัน ซึ่งระบบยังไม่ได้ประเมินให้/,
      ),
    ).toBeInTheDocument()
  })

  it('always discloses concurrency and entitlement were not assessed (hard-coded false every Sprint 11 evaluation)', () => {
    render(<EotEvaluationPanel eot={makeEot({ state: 'success', result: substantiated })} canEvaluate />)
    expect(screen.getByText(/ยังไม่ได้ประเมินคุณสมบัติตามสัญญา/)).toBeInTheDocument()
    expect(screen.getByText(/ยังไม่ได้พิจารณาความล่าช้าที่เกิดพร้อมกัน/)).toBeInTheDocument()
  })

  it('shows the confidence and criticality-basis badges', () => {
    render(<EotEvaluationPanel eot={makeEot({ state: 'success', result: substantiated })} canEvaluate />)
    expect(screen.getByText(/Substantiated/)).toBeInTheDocument()
    expect(screen.getByText('ตามตารางเวลา ณ วันที่เกิดเหตุ')).toBeInTheDocument()
  })

  it('flags a Provisional result with an extra, unmissable warning', () => {
    render(<EotEvaluationPanel eot={makeEot({ state: 'success', result: { ...substantiated, confidence: 'Provisional' } })} canEvaluate />)
    expect(screen.getByText(/ไม่ควรใช้เป็นตัวเลขยืนยันสำหรับการเรียกร้องสิทธิ์/)).toBeInTheDocument()
  })

  it('renders the headline days figure', () => {
    render(<EotEvaluationPanel eot={makeEot({ state: 'success', result: substantiated })} canEvaluate />)
    expect(screen.getByTestId('eot-eligible-days')).toHaveTextContent('2')
  })

  it('renders the drivers table with activity code/name and the non-summing caveat', () => {
    render(<EotEvaluationPanel eot={makeEot({ state: 'success', result: substantiated })} canEvaluate />)
    expect(screen.getByText('B')).toBeInTheDocument()
    expect(screen.getByText('งานโครงสร้าง B')).toBeInTheDocument()
    expect(screen.getByText(/ไม่จำเป็นต้องรวมกันได้เท่ากับผลรวม/)).toBeInTheDocument()
  })

  it('discloses excluded source rows and their reason', async () => {
    render(<EotEvaluationPanel eot={makeEot({ state: 'success', result: substantiated })} canEvaluate />)
    await userEvent.click(screen.getByText(/บันทึกที่ไม่ถูกนับ/))
    expect(screen.getByText('ไม่ใช่วันทำงานตามปฏิทินโครงการ')).toBeInTheDocument()
  })

  it('idle state prompts to run an evaluation rather than showing stale/zero numbers', () => {
    render(<EotEvaluationPanel eot={makeEot()} canEvaluate />)
    expect(screen.getByText(/ยังไม่ได้ประเมิน EOT ในเซสชันนี้/)).toBeInTheDocument()
    expect(screen.queryByText('2')).not.toBeInTheDocument()
  })

  it('clicking "ประเมิน EOT" calls eot.evaluate', async () => {
    const evaluate = vi.fn().mockResolvedValue(null)
    render(<EotEvaluationPanel eot={makeEot({ evaluate })} canEvaluate />)
    await userEvent.click(screen.getByRole('button', { name: 'ประเมิน EOT' }))
    expect(evaluate).toHaveBeenCalledTimes(1)
  })

  it('offers "ประเมิน EOT อีกครั้ง" once a result already exists (re-evaluating is always a new record)', () => {
    render(<EotEvaluationPanel eot={makeEot({ state: 'success', result: substantiated })} canEvaluate />)
    expect(screen.getByRole('button', { name: 'ประเมิน EOT อีกครั้ง' })).toBeInTheDocument()
  })

  it('hides the evaluate button when the role is not permitted', () => {
    render(<EotEvaluationPanel eot={makeEot()} canEvaluate={false} />)
    expect(screen.queryByRole('button', { name: /ประเมิน EOT/ })).not.toBeInTheDocument()
    expect(screen.getByText(/บทบาทของคุณไม่สามารถประเมิน EOT ได้/)).toBeInTheDocument()
  })

  it('shows the server error message on failure', () => {
    render(<EotEvaluationPanel eot={makeEot({ state: 'error', error: 'โครงการยังไม่มีประวัติการคำนวณ CPM' })} canEvaluate />)
    expect(screen.getByText('โครงการยังไม่มีประวัติการคำนวณ CPM')).toBeInTheDocument()
  })
})
