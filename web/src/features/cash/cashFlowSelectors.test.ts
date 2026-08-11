import { describe, expect, it } from 'vitest'
import {
  describeActualCostEntryCount,
  describeReceiptsUnavailable,
  describeWarning,
  toneForSign,
} from './cashFlowSelectors'

describe('describeWarning', () => {
  it('translates the CashFlowPeriodRestated code (ADR-0009/ADR-0013 §6.4 restatement)', () => {
    expect(describeWarning('CashFlowPeriodRestated')).toContain('Restated')
  })

  it('translates the shared EVM-engine warning codes too', () => {
    expect(describeWarning('EarnedValueExceedsBudget')).toContain('เกินงบประมาณ')
    expect(describeWarning('ActualCostIsNegative')).toContain('ติดลบ')
  })

  it('never hides an unmapped code — falls back to the raw code', () => {
    expect(describeWarning('SomeFutureWarningCode')).toBe('SomeFutureWarningCode')
  })
})

describe('describeReceiptsUnavailable', () => {
  it('translates the one known reason', () => {
    expect(describeReceiptsUnavailable('PaymentCertificatesNotYetImplemented')).toContain('Sprint 9')
  })

  it('degrades to an honest generic line for a null reason rather than crashing', () => {
    expect(describeReceiptsUnavailable(null)).toBeTruthy()
  })
})

describe('describeActualCostEntryCount (actual-cost.md §7.6 AC-5/AC-6 distinction)', () => {
  it('AC-5: zero entries -> "no cost recorded" (never conflated with a genuine net-zero)', () => {
    expect(describeActualCostEntryCount('0.00', 0)).toBe('ยังไม่มีการบันทึกต้นทุน (Actual Cost)')
  })

  it('AC-6: entries exist but net to exactly zero -> "net zero from N entries", distinct wording from AC-5', () => {
    const result = describeActualCostEntryCount('0.00', 2)
    expect(result).toContain('2')
    expect(result).toContain('ต้นทุนสุทธิเป็นศูนย์')
    expect(result).not.toBe(describeActualCostEntryCount('0.00', 0))
  })

  it('a normal non-zero balance shows the real entry count', () => {
    const result = describeActualCostEntryCount('253100000.00', 42)
    expect(result).toContain('42')
    expect(result).not.toContain('ยังไม่มีการบันทึกต้นทุน')
    expect(result).not.toContain('สุทธิเป็นศูนย์')
  })
})

describe('toneForSign (Net Cash Position)', () => {
  it('null -> neutral (honest "not available yet", never a fabricated tone)', () => {
    expect(toneForSign(null)).toBe('neutral')
  })

  it('>= 0 -> success (receipts cover spend)', () => {
    expect(toneForSign('0.00')).toBe('success')
    expect(toneForSign('120000.00')).toBe('success')
  })

  it('< 0 -> danger (contractor funding a gap) — matches the prototype\'s own literal color on this figure', () => {
    expect(toneForSign('-14700000.00')).toBe('danger')
  })
})
