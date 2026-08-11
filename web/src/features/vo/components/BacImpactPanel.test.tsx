import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import { BacImpactPanel } from './BacImpactPanel'
import type { BacImpactPanelProps } from './BacImpactPanel'

/** `docs/specs/variation-order/domain-rules.md` §11 traceability table names this exact file
 * against fixtures V-9 (the BAC move) and V-5b (the escalation-threshold boundary). */

function renderPanel(overrides: Partial<BacImpactPanelProps> = {}) {
  const props: BacImpactPanelProps = {
    amount: 10_000_000,
    bacBefore: 100_000_000,
    contractValueBefore: 100_000_000,
    cumulativeApprovedVoBefore: null,
    escalationBaselineContractValue: null,
    escalationThresholdPct: null,
    ...overrides,
  }
  return render(<BacImpactPanel {...props} />)
}

describe('BacImpactPanel', () => {
  it('V-9: shows BAC and ContractValue moving from 100,000,000.00 to 110,000,000.00 on a +10,000,000.00 Add VO', () => {
    renderPanel()

    expect(screen.getByText('+10,000,000.00 บาท')).toBeInTheDocument()
    // Both the BAC row and the ContractValue row read "100,000,000.00 → 110,000,000.00 บาท" in this
    // fixture (bacBefore === contractValueBefore) — assert both are present rather than a single
    // ambiguous query.
    expect(screen.getAllByText('100,000,000.00 → 110,000,000.00 บาท')).toHaveLength(2)
  })

  it('a Deduct VO renders the true negative sign — never abs() — on the amount and both before/after rows', () => {
    renderPanel({ amount: -800_000, bacBefore: 485_000_000, contractValueBefore: 485_000_000 })

    expect(screen.getByText('-800,000.00 บาท')).toBeInTheDocument()
    expect(screen.getAllByText('485,000,000.00 → 484,200,000.00 บาท').length).toBe(2)
    // The amount row must render in the danger tone for a deduction, not the "Add" tone.
    expect(screen.getByText('-800,000.00 บาท')).toHaveClass('text-danger')
  })

  it('an Add VO renders the amount with an explicit "+" and the secondary (Add) tone', () => {
    renderPanel({ amount: 2_400_000 })
    const amountEl = screen.getByText('+2,400,000.00 บาท')
    expect(amountEl).toHaveClass('text-secondary')
  })

  it('threshold = null renders "not configured", never a 0% reading', () => {
    renderPanel({ escalationThresholdPct: null, cumulativeApprovedVoBefore: 999_000_000, escalationBaselineContractValue: 1 })

    expect(screen.getByText(/ไม่ได้ตั้งค่าเกณฑ์ escalation/)).toBeInTheDocument()
    expect(screen.queryByText('0.00%')).not.toBeInTheDocument()
  })

  it('missing prior-VO/baseline data renders an honest "cannot compute" note, not a guessed percentage', () => {
    renderPanel({ escalationThresholdPct: 10, cumulativeApprovedVoBefore: null, escalationBaselineContractValue: null })

    expect(screen.getByText(/ยังไม่สามารถคำนวณ % VO สะสม/)).toBeInTheDocument()
  })

  it('V-5a: exactly at the threshold (10.00%) shows the percentage with no crossing warning', () => {
    renderPanel({
      amount: 3_200_000,
      cumulativeApprovedVoBefore: 45_300_000,
      escalationBaselineContractValue: 485_000_000,
      escalationThresholdPct: 10,
    })

    // "10.00%" legitimately appears twice — the headline pct value and the "(...เกณฑ์ 10.00%)"
    // clause both contain it as a substring — assert presence, not a single unique element.
    expect(screen.getAllByText(/10\.00%/).length).toBeGreaterThan(0)
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('V-5b: 10.004000% (unrounded) DOES render the crossing warning — rounding must never hide it', () => {
    renderPanel({
      amount: 3_200_000,
      cumulativeApprovedVoBefore: 45_319_400,
      escalationBaselineContractValue: 485_000_000,
      escalationThresholdPct: 10,
      escalationRole: 'Executive',
    })

    const warning = screen.getByRole('alert')
    expect(warning).toHaveTextContent('เกินเกณฑ์ escalation')
    expect(warning).toHaveTextContent('Executive')
  })

  it('supports a custom title (e.g. for showing the actual recorded figures of an already-Approved VO)', () => {
    renderPanel({ title: 'ผลกระทบที่บันทึกไว้ตอนอนุมัติ' })
    expect(screen.getByText('ผลกระทบที่บันทึกไว้ตอนอนุมัติ')).toBeInTheDocument()
  })
})
