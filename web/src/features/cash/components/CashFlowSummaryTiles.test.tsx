import { render, screen, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { CashFlowSummaryTiles } from './CashFlowSummaryTiles'
import type { CashFlowResponseDto } from '../types'

const baseCashFlow: CashFlowResponseDto = {
  projectId: 'project-1',
  dataDate: '2026-07-11T00:00:00+07:00',
  bac: '485000000.00',
  pvCumulative: '285180000.00',
  evCumulative: '262970000.00',
  acCumulative: '253100000.00',
  actualCostEntryCount: 42,
  periods: [],
  receipts: {
    isAvailable: false,
    cumulative: null,
    periods: [],
    unavailableReason: 'PaymentCertificatesNotYetImplemented',
  },
  netCashPosition: null,
  warnings: [],
}

/** The tile grid only — excludes the note paragraph below it, which deliberately repeats the tile
 * labels ("รับเงินสะสม"/"ต้นทุนเกิดขึ้นจริงสะสม (AC)") in bold as part of its own explanatory text
 * (ADR-0013 §5's separation note), so an unscoped `getByText` on those exact labels is ambiguous by
 * design, not a bug — scope to the grid for tile-label assertions. */
function renderTiles(cashFlow: CashFlowResponseDto) {
  render(<CashFlowSummaryTiles cashFlow={cashFlow} />)
  return within(screen.getByTestId('cash-flow-summary-tiles'))
}

describe('CashFlowSummaryTiles (S8-FE-02)', () => {
  it('renders exactly 4 tiles matching the prototype layout', () => {
    render(<CashFlowSummaryTiles cashFlow={baseCashFlow} />)
    const grid = screen.getByTestId('cash-flow-summary-tiles')
    expect(grid.children).toHaveLength(4)
  })

  it("today's real state: receipts unavailable -> honest \"—\", never fabricated, with the Sprint 9 reason; AC shows the real (possibly zero) figure", () => {
    const tiles = renderTiles(baseCashFlow)

    expect(tiles.getByText('รับเงินสะสม')).toBeInTheDocument()
    // Both the receipts and Retention tiles mention "Sprint 9" (two independent Sprint-9-gated
    // features) — match the receipts tile's own distinguishing phrase, not the bare year/sprint.
    expect(tiles.getByText(/Payment Certificate.*Sprint 9/)).toBeInTheDocument()
    expect(tiles.getByText('253.10 MB')).toBeInTheDocument() // real AC, not "—"
  })

  it('ADR-0013 corrected label: the AC tile reads "ต้นทุนเกิดขึ้นจริงสะสม (AC)", never the prototype\'s literal "จ่ายจริงสะสม (AC)" (cash-paid framing)', () => {
    const tiles = renderTiles(baseCashFlow)

    expect(tiles.getByText('ต้นทุนเกิดขึ้นจริงสะสม (AC)')).toBeInTheDocument()
    expect(screen.queryByText('จ่ายจริงสะสม (AC)')).not.toBeInTheDocument()
  })

  it('Retention has no backing field at all yet — rendered as an explicit not-available tile, never a fabricated number', () => {
    const tiles = renderTiles(baseCashFlow)
    expect(tiles.getByText('Retention สะสม')).toBeInTheDocument()
    expect(tiles.getByText(/ProjectFinanceLedger \(Sprint 9\)/)).toBeInTheDocument()
  })

  it('Net Cash Position renders "—" (never 0) while null, and a real value with the correct tone once available', () => {
    const { rerender } = render(<CashFlowSummaryTiles cashFlow={baseCashFlow} />)
    // "—" appears at least twice here (receipts tile + net position tile, both null today) — never
    // a fabricated 0/number for either.
    expect(screen.getAllByText('—').length).toBeGreaterThanOrEqual(2)
    expect(screen.queryByText('-0.00 MB')).not.toBeInTheDocument()
    expect(screen.queryByText('0.00 MB')).not.toBeInTheDocument()

    rerender(<CashFlowSummaryTiles cashFlow={{ ...baseCashFlow, netCashPosition: '-14700000.00' }} />)
    const value = screen.getByText('-14.70 MB')
    expect(value.className).toContain('text-danger')
  })

  it('AC-5/AC-6 discipline: distinguishes "no entries" from "entries net to zero" in the AC tile caption', () => {
    const { rerender } = render(
      <CashFlowSummaryTiles cashFlow={{ ...baseCashFlow, acCumulative: '0.00', actualCostEntryCount: 0 }} />,
    )
    expect(screen.getByText('ยังไม่มีการบันทึกต้นทุน (Actual Cost)')).toBeInTheDocument()

    rerender(<CashFlowSummaryTiles cashFlow={{ ...baseCashFlow, acCumulative: '0.00', actualCostEntryCount: 2 }} />)
    expect(screen.getByText(/ต้นทุนสุทธิเป็นศูนย์จาก 2 รายการ/)).toBeInTheDocument()
  })

  it('ADR-0013 §5: states explicitly that receipts and AC are separate ledgers that must not be combined', () => {
    render(<CashFlowSummaryTiles cashFlow={baseCashFlow} />)
    expect(screen.getByText(/เป็นบัญชีคนละชุดกัน/)).toBeInTheDocument()
  })

  it('receipts tile renders the real cumulative figure once available (Sprint 9 forward-compatibility), never assumed permanently unavailable', () => {
    render(
      <CashFlowSummaryTiles
        cashFlow={{
          ...baseCashFlow,
          receipts: { isAvailable: true, cumulative: '238400000.00', periods: [], unavailableReason: null },
        }}
      />,
    )
    expect(screen.getByText('238.40 MB')).toBeInTheDocument()
  })
})
