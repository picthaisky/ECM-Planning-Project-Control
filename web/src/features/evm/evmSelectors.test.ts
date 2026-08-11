import { describe, expect, it } from 'vitest'
import {
  UI_EAC_VARIANTS,
  buildSCurvePoints,
  findVariantResult,
  resolveTcpi,
  toneForRatioThreshold,
  toneForSign,
} from './evmSelectors'
import type { EacVariantResponseDto, EvmResponseDto, EvmSnapshotDto } from './types'

function snapshot(overrides: Partial<EvmSnapshotDto>): EvmSnapshotDto {
  return {
    snapshotId: 'snap',
    projectId: 'project-1',
    dataDate: '2026-01-31T00:00:00+07:00',
    bac: '1000000.00',
    pv: '100000.00',
    ev: '90000.00',
    ac: '95000.00',
    eacVariant: 'CpiBased',
    performanceFactor: null,
    eac: null,
    etc: null,
    vac: null,
    createdAt: '2026-02-01T00:00:00+07:00',
    ...overrides,
  }
}

function variant(overrides: Partial<EacVariantResponseDto>): EacVariantResponseDto {
  return {
    variant: 'CpiBased',
    performanceFactor: null,
    etc: null,
    eac: null,
    vac: null,
    computable: false,
    reason: null,
    ...overrides,
  }
}

describe('UI_EAC_VARIANTS', () => {
  it('S14-FE-02: exposes all 5 engine variants (ADR-0007), index-based first, in this fixed order', () => {
    expect(UI_EAC_VARIANTS).toEqual(['CpiBased', 'Atypical', 'CpiSpiBased', 'BottomUpEtc', 'CustomPf'])
  })
})

describe('buildSCurvePoints (ADR-0009)', () => {
  const evm: Pick<EvmResponseDto, 'dataDate' | 'pv' | 'ev' | 'ac'> = {
    dataDate: '2026-06-30T00:00:00+07:00',
    pv: '400000.00',
    ev: '300000.00',
    ac: '350000.00',
  }

  it('appends the live EVM reading as the last point', () => {
    const points = buildSCurvePoints(evm, [])
    expect(points).toEqual([{ dataDate: evm.dataDate, pv: evm.pv, ev: evm.ev, ac: evm.ac }])
  })

  it('DoD: includes a snapshot strictly before the live data date as a historical point', () => {
    const past = snapshot({ dataDate: '2026-04-30T00:00:00+07:00', pv: '200000.00', ev: '180000.00', ac: '190000.00' })
    const points = buildSCurvePoints(evm, [past])

    expect(points).toHaveLength(2)
    expect(points[0]).toEqual({ dataDate: past.dataDate, pv: past.pv, ev: past.ev, ac: past.ac })
    expect(points[1]).toEqual({ dataDate: evm.dataDate, pv: evm.pv, ev: evm.ev, ac: evm.ac })
  })

  it('excludes a snapshot dated on or after the live data date (the live reading is the authority for "now")', () => {
    const sameDay = snapshot({ dataDate: evm.dataDate })
    const future = snapshot({ dataDate: '2026-12-31T00:00:00+07:00' })
    const points = buildSCurvePoints(evm, [sameDay, future])

    expect(points).toHaveLength(1)
    expect(points[0].dataDate).toBe(evm.dataDate)
  })

  it('sorts historical snapshots ascending by date even if the input array is out of order (never trusts caller ordering blindly)', () => {
    const later = snapshot({ dataDate: '2026-05-31T00:00:00+07:00' })
    const earlier = snapshot({ dataDate: '2026-02-28T00:00:00+07:00' })
    const points = buildSCurvePoints(evm, [later, earlier])

    expect(points.map((p) => p.dataDate)).toEqual([earlier.dataDate, later.dataDate, evm.dataDate])
  })
})

describe('findVariantResult', () => {
  it('finds the matching entry by variant name', () => {
    const evm = { variants: [variant({ variant: 'CpiBased', eac: '1166666.67' }), variant({ variant: 'Atypical' })] }
    expect(findVariantResult(evm, 'CpiBased')?.eac).toBe('1166666.67')
  })

  it('returns undefined for a variant not present in the array (defensive)', () => {
    const evm = { variants: [variant({ variant: 'Atypical' })] }
    expect(findVariantResult(evm, 'CpiBased')).toBeUndefined()
  })
})

describe('resolveTcpi', () => {
  it('shows TCPI_BAC while the selected variant is still within budget (VAC >= 0, Fixture D)', () => {
    const evm = { tcpiBac: '0.933333', tcpiEac: '0.909091' }
    const selected = variant({ variant: 'CpiBased', vac: '45454.55', computable: true })
    expect(resolveTcpi(evm, selected)).toEqual({ value: '0.933333', basis: 'BAC' })
  })

  it('switches to TCPI_EAC once the selected variant has overrun (VAC < 0, Fixture A)', () => {
    const evm = { tcpiBac: '1.076923', tcpiEac: '0.857143' }
    const selected = variant({ variant: 'CpiBased', vac: '-166666.67', computable: true })
    expect(resolveTcpi(evm, selected)).toEqual({ value: '0.857143', basis: 'EAC' })
  })

  it('falls back to TCPI_BAC when the selected variant is not computable (VAC null — the common case while AC is always 0)', () => {
    const evm = { tcpiBac: '1.076923', tcpiEac: null }
    const selected = variant({ variant: 'CpiBased', vac: null, computable: false, reason: 'NoActualCost' })
    expect(resolveTcpi(evm, selected)).toEqual({ value: '1.076923', basis: 'BAC' })
  })

  it('falls back to TCPI_BAC when there is no selected-variant result at all (defensive)', () => {
    const evm: Pick<EvmResponseDto, 'tcpiBac' | 'tcpiEac'> = { tcpiBac: '1.0769', tcpiEac: '0.8571' }
    expect(resolveTcpi(evm, undefined)).toEqual({ value: '1.0769', basis: 'BAC' })
  })

  it('falls back to TCPI_BAC when VAC is negative but tcpiEac itself is null (never crash/show undefined)', () => {
    const evm = { tcpiBac: '1.0769', tcpiEac: null }
    const selected = variant({ vac: '-1.00', computable: true })
    expect(resolveTcpi(evm, selected)).toEqual({ value: '1.0769', basis: 'BAC' })
  })
})

describe('toneForSign (SV/CV/VAC)', () => {
  it('is success for a zero-or-positive value (favourable)', () => {
    expect(toneForSign('0.00')).toBe('success')
    expect(toneForSign('45454.55')).toBe('success')
  })

  it('is danger for a negative value (unfavourable)', () => {
    expect(toneForSign('-166666.67')).toBe('danger')
  })

  it('is neutral for null (never a fabricated favourable/unfavourable colour on missing data)', () => {
    expect(toneForSign(null)).toBe('neutral')
  })

  it('is neutral for a non-numeric string (defensive)', () => {
    expect(toneForSign('not-a-number')).toBe('neutral')
  })

  it(
    'DoD: never assumes a fixed variant ordering — the same rule call, applied to each fixture\'s own ' +
      'VAC, reproduces evm-formulas.md Fixture A (unfavourable, CPI<1) AND Fixture D (favourable, ' +
      'CPI>1) correctly even though the cheapest/most-expensive variant reverses between them',
    () => {
      // Fixture A (unfavourable): CpiBased VAC=-166,666.67, Atypical VAC=-50,000.00, CpiSpiBased VAC=-438,888.89.
      expect(toneForSign('-166666.67')).toBe('danger')
      expect(toneForSign('-50000.00')).toBe('danger')
      expect(toneForSign('-438888.89')).toBe('danger')

      // Fixture D (favourable, CPI>1 — ordering mirrors/reverses Fixture A): CpiBased VAC=+45,454.55,
      // Atypical VAC=+20,000.00, CpiSpiBased VAC=+68,595.04 — CpiSpiBased is now the *most*
      // favourable (opposite of Fixture A), yet the same sign-only rule still colours all three
      // correctly with no per-variant branch.
      expect(toneForSign('45454.55')).toBe('success')
      expect(toneForSign('20000.00')).toBe('success')
      expect(toneForSign('68595.04')).toBe('success')
    },
  )
})

describe('toneForRatioThreshold (SPI/CPI)', () => {
  it('is success at or above the threshold (default 1)', () => {
    expect(toneForRatioThreshold('1.00')).toBe('success')
    expect(toneForRatioThreshold('1.04')).toBe('success')
  })

  it('is danger below the threshold', () => {
    expect(toneForRatioThreshold('0.92')).toBe('danger')
  })

  it('is neutral for null', () => {
    expect(toneForRatioThreshold(null)).toBe('neutral')
  })

  it('supports a custom threshold', () => {
    expect(toneForRatioThreshold('0.95', 0.9)).toBe('success')
  })
})
