import { describe, expect, it } from 'vitest'
import { resolveRetentionCapStatus } from './certificateBreakdown'

describe('resolveRetentionCapStatus', () => {
  it('is unknown while the project has not loaded (contractValue null)', () => {
    expect(
      resolveRetentionCapStatus({
        grossCertifiedAmount: 1_000_000,
        actualRetentionAmount: 50_000,
        retentionRatePct: 5,
        retentionCapPercentage: 10,
        contractValue: null,
      }),
    ).toEqual({ kind: 'unknown' })
  })

  it('is uncapped for the Thai-standard contract shape (RetentionCapPercentage null)', () => {
    expect(
      resolveRetentionCapStatus({
        grossCertifiedAmount: 21_600_000,
        actualRetentionAmount: 1_080_000,
        retentionRatePct: 5,
        retentionCapPercentage: null,
        contractValue: 485_000_000,
      }),
    ).toEqual({ kind: 'uncapped' })
  })

  it('P1 (payment-retention.md): capped config but the cap is not binding this period', () => {
    // C=485,000,000; r=5.00; c is not part of P1 (uncapped) — use a generous cap instead so this
    // fixture isolates "capped, not binding" from "uncapped" (covered by the test above).
    // R_k = 1,080,000.00 for G_k = 21,600,000.00 at r=5% — exactly the nominal figure, so a cap of
    // 5% of contract (24,250,000.00) is nowhere near binding.
    const status = resolveRetentionCapStatus({
      grossCertifiedAmount: 21_600_000,
      actualRetentionAmount: 1_080_000,
      retentionRatePct: 5,
      retentionCapPercentage: 5,
      contractValue: 485_000_000,
    })

    expect(status.kind).toBe('capped')
    if (status.kind !== 'capped') throw new Error('unreachable')
    expect(status.capAmount).toBeCloseTo(24_250_000, 2)
    expect(status.isBindingThisPeriod).toBe(false)
  })

  it('P3 (payment-retention.md): the cap bites — actual retention below the naive rate figure', () => {
    // C=100,000,000.00; r=10.00; c=5.00 => R^max=5,000,000.00; G_k=8,000,000.00.
    // Naive (uncapped) retention would be 800,000.00; the backend's CertificateCalculator already
    // clamped RetentionAmount to 500,000.00 (headroom-limited) — this is what the DTO actually
    // carries, so the indicator must recognise the mismatch and flag it as binding.
    const status = resolveRetentionCapStatus({
      grossCertifiedAmount: 8_000_000,
      actualRetentionAmount: 500_000,
      retentionRatePct: 10,
      retentionCapPercentage: 5,
      contractValue: 100_000_000,
    })

    expect(status.kind).toBe('capped')
    if (status.kind !== 'capped') throw new Error('unreachable')
    expect(status.capAmount).toBeCloseTo(5_000_000, 2)
    expect(status.isBindingThisPeriod).toBe(true)
  })

  it('P3b (payment-retention.md): cap fully consumed — actual retention is zero while nominal is not', () => {
    const status = resolveRetentionCapStatus({
      grossCertifiedAmount: 5_000_000,
      actualRetentionAmount: 0,
      retentionRatePct: 10,
      retentionCapPercentage: 5,
      contractValue: 100_000_000,
    })

    expect(status.kind).toBe('capped')
    if (status.kind !== 'capped') throw new Error('unreachable')
    expect(status.isBindingThisPeriod).toBe(true)
  })

  it('shows the cap ceiling but cannot judge "binding" when the rate itself is unknown', () => {
    const status = resolveRetentionCapStatus({
      grossCertifiedAmount: 8_000_000,
      actualRetentionAmount: 500_000,
      retentionRatePct: null,
      retentionCapPercentage: 5,
      contractValue: 100_000_000,
    })

    expect(status.kind).toBe('capped')
    if (status.kind !== 'capped') throw new Error('unreachable')
    expect(status.capAmount).toBeCloseTo(5_000_000, 2)
    expect(status.isBindingThisPeriod).toBe(false)
  })
})
