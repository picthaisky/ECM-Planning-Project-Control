import { describe, expect, it } from 'vitest'
import { render, screen, within } from '@testing-library/react'
import { EvmMetricsGrid } from './EvmMetricsGrid'
import type { EacVariantResponseDto, EvmResponseDto } from '../types'

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

// evm-formulas.md Fixture A (typical, behind schedule & over budget — unfavourable, CPI<1).
const fixtureA: EvmResponseDto = {
  projectId: 'project-1',
  dataDate: '2026-06-30T00:00:00+07:00',
  bac: '1000000.00',
  pv: '400000.00',
  ev: '300000.00',
  ac: '350000.00',
  sv: '-100000.00',
  cv: '-50000.00',
  spi: '0.750000',
  cpi: '0.857143',
  tcpiBac: '1.076923',
  tcpiEac: '0.857143',
  selectedVariant: 'CpiBased',
  variants: [
    variant({ variant: 'CpiBased', performanceFactor: '1.166667', etc: '816666.67', eac: '1166666.67', vac: '-166666.67', computable: true }),
    variant({ variant: 'Atypical', performanceFactor: '1.000000', etc: '700000.00', eac: '1050000.00', vac: '-50000.00', computable: true }),
    variant({ variant: 'CpiSpiBased', performanceFactor: '1.555556', etc: '1088888.89', eac: '1438888.89', vac: '-438888.89', computable: true }),
  ],
  warnings: [],
}

// evm-formulas.md Fixture D (favourable, CPI>1 — ordering reverses relative to Fixture A).
const fixtureD: EvmResponseDto = {
  projectId: 'project-1',
  dataDate: '2026-06-30T00:00:00+07:00',
  bac: '500000.00',
  pv: '200000.00',
  ev: '220000.00',
  ac: '200000.00',
  sv: '20000.00',
  cv: '20000.00',
  spi: '1.100000',
  cpi: '1.100000',
  tcpiBac: '0.933333',
  tcpiEac: '0.909091',
  selectedVariant: 'CpiSpiBased',
  variants: [
    variant({ variant: 'CpiBased', etc: '254545.45', eac: '454545.45', vac: '45454.55', computable: true }),
    variant({ variant: 'Atypical', etc: '280000.00', eac: '480000.00', vac: '20000.00', computable: true }),
    variant({ variant: 'CpiSpiBased', etc: '231404.96', eac: '431404.96', vac: '68595.04', computable: true }),
  ],
  warnings: [],
}

function findVariant(evm: EvmResponseDto, variantName: string) {
  return evm.variants.find((v) => v.variant === variantName)
}

describe('EvmMetricsGrid', () => {
  it('DoD: renders exactly 12 tiles', () => {
    render(<EvmMetricsGrid evm={fixtureA} selectedVariantResult={findVariant(fixtureA, 'CpiBased')} />)
    const grid = screen.getByTestId('evm-metrics-grid')
    expect(grid.children).toHaveLength(12)
  })

  it('DoD: every value matches the API response exactly (Fixture A, CpiBased selected)', () => {
    render(<EvmMetricsGrid evm={fixtureA} selectedVariantResult={findVariant(fixtureA, 'CpiBased')} />)

    expect(screen.getByText('1,000,000.00 บาท')).toBeInTheDocument() // BAC
    expect(screen.getByText('400,000.00 บาท')).toBeInTheDocument() // PV
    expect(screen.getByText('300,000.00 บาท')).toBeInTheDocument() // EV
    expect(screen.getByText('350,000.00 บาท')).toBeInTheDocument() // AC
    expect(screen.getByText('-100,000.00 บาท')).toBeInTheDocument() // SV
    expect(screen.getByText('-50,000.00 บาท')).toBeInTheDocument() // CV
    expect(screen.getByText('0.75')).toBeInTheDocument() // SPI (formatRatio, 2dp)
    // CPI (0.857143 rounded to 2dp) — Fixture A's CpiBased has overrun (VAC < 0), so the TCPI tile
    // also shows TCPI_EAC = 0.857143 here too (a genuine coincidence of this fixture, not a bug —
    // asserted precisely in the dedicated "TCPI switches…" test below), hence 2 matches.
    expect(screen.getAllByText('0.86')).toHaveLength(2)
    expect(screen.getByText('1,166,666.67 บาท')).toBeInTheDocument() // EAC (CpiBased)
    expect(screen.getByText('816,666.67 บาท')).toBeInTheDocument() // ETC
    expect(screen.getByText('-166,666.67 บาท')).toBeInTheDocument() // VAC
  })

  it('switching the resolved variant updates EAC/ETC/VAC together, with no stale tile (S7-FE-02 DoD)', () => {
    const { rerender } = render(
      <EvmMetricsGrid evm={fixtureA} selectedVariantResult={findVariant(fixtureA, 'CpiBased')} />,
    )
    expect(screen.getByText('1,166,666.67 บาท')).toBeInTheDocument()

    rerender(<EvmMetricsGrid evm={fixtureA} selectedVariantResult={findVariant(fixtureA, 'CpiSpiBased')} />)

    expect(screen.queryByText('1,166,666.67 บาท')).not.toBeInTheDocument()
    expect(screen.getByText('1,438,888.89 บาท')).toBeInTheDocument() // EAC
    expect(screen.getByText('1,088,888.89 บาท')).toBeInTheDocument() // ETC
    expect(screen.getByText('-438,888.89 บาท')).toBeInTheDocument() // VAC
  })

  it('renders "—" for null metrics, never 0 (AC=0-gap scenario: NoActualCost suppresses CpiBased)', () => {
    const noActualCost: EvmResponseDto = {
      ...fixtureA,
      ac: '0.00',
      sv: fixtureA.sv,
      cv: '300000.00',
      spi: fixtureA.spi,
      cpi: null,
      tcpiEac: null,
      variants: [variant({ variant: 'CpiBased', computable: false, reason: 'NoActualCost' })],
    }
    render(<EvmMetricsGrid evm={noActualCost} selectedVariantResult={findVariant(noActualCost, 'CpiBased')} />)

    // Each tile's own outer container carries `data-state` (StatTile) — scope to it so this checks
    // the *value* paragraph specifically, never a coincidental substring match against the caption.
    const cpiTile = screen.getByText('CPI').closest('[data-state]') as HTMLElement
    expect(within(cpiTile).getByText('—')).toBeInTheDocument()
    expect(within(cpiTile).queryByText('0')).not.toBeInTheDocument()
    expect(within(cpiTile).queryByText('0.00')).not.toBeInTheDocument()

    const eacTile = screen.getByText('EAC').closest('[data-state]') as HTMLElement
    expect(within(eacTile).getByText('—')).toBeInTheDocument()

    const etcTile = screen.getByText('ETC').closest('[data-state]') as HTMLElement
    expect(within(etcTile).getByText('—')).toBeInTheDocument()

    const vacTile = screen.getByText('VAC').closest('[data-state]') as HTMLElement
    expect(within(vacTile).getByText('—')).toBeInTheDocument()
  })

  it('EV tile uses the gold tone, AC uses the danger tone, regardless of variant (fixed, not variant-derived)', () => {
    render(<EvmMetricsGrid evm={fixtureA} selectedVariantResult={findVariant(fixtureA, 'CpiBased')} />)
    const evTile = screen.getByText('EV (BCWP)').closest('div') as HTMLElement
    const acTile = screen.getByText('AC (ACWP)').closest('div') as HTMLElement
    expect(within(evTile).getByText('300,000.00 บาท').className).toMatch(/text-gold/)
    expect(within(acTile).getByText('350,000.00 บาท').className).toMatch(/text-danger/)
  })

  it('BAC/EAC/ETC tiles stay neutral (navy) in both an unfavourable (Fixture A) and a favourable (Fixture D) scenario — never colour-coded by variant identity', () => {
    const { rerender } = render(
      <EvmMetricsGrid evm={fixtureA} selectedVariantResult={findVariant(fixtureA, 'CpiSpiBased')} />,
    )
    let eacValue = screen.getByText('1,438,888.89 บาท')
    expect(eacValue.className).toMatch(/text-navy/)
    expect(eacValue.className).not.toMatch(/text-danger|text-success/)

    rerender(<EvmMetricsGrid evm={fixtureD} selectedVariantResult={findVariant(fixtureD, 'CpiSpiBased')} />)
    eacValue = screen.getByText('431,404.96 บาท')
    expect(eacValue.className).toMatch(/text-navy/)
    expect(eacValue.className).not.toMatch(/text-danger|text-success/)
  })

  it('VAC tile colour is derived from its own sign, correctly for both the unfavourable and the favourable (reversed-ordering) fixture', () => {
    const { rerender } = render(
      <EvmMetricsGrid evm={fixtureA} selectedVariantResult={findVariant(fixtureA, 'CpiSpiBased')} />,
    )
    // Fixture A: CpiSpiBased VAC = -438,888.89 (unfavourable) -> danger.
    expect(screen.getByText('-438,888.89 บาท').className).toMatch(/text-danger/)

    // Fixture D: CpiSpiBased VAC = +68,595.04 (favourable, and now the *largest* VAC of the three —
    // the opposite rank from Fixture A) -> success. Proves the tone is read from the number, not
    // from "CpiSpiBased is usually the worst one".
    rerender(<EvmMetricsGrid evm={fixtureD} selectedVariantResult={findVariant(fixtureD, 'CpiSpiBased')} />)
    expect(screen.getByText('68,595.04 บาท').className).toMatch(/text-success/)
  })

  it('TCPI switches from BAC-basis to EAC-basis once the selected variant has overrun (evm-formulas.md rule)', () => {
    const { rerender } = render(
      <EvmMetricsGrid evm={fixtureD} selectedVariantResult={findVariant(fixtureD, 'CpiBased')} />,
    )
    // Fixture D, CpiBased: VAC=+45,454.55 (no overrun) -> TCPI_BAC = 0.933333 -> "0.93". Scoped to
    // the TCPI tile itself since SPI/CPI/etc. could otherwise coincide with the same rounded text.
    let tcpiTile = screen.getByText('TCPI (BAC)').closest('[data-state]') as HTMLElement
    expect(within(tcpiTile).getByText('0.93')).toBeInTheDocument()

    rerender(<EvmMetricsGrid evm={fixtureA} selectedVariantResult={findVariant(fixtureA, 'CpiBased')} />)
    // Fixture A, CpiBased: VAC=-166,666.67 (overrun) -> TCPI_EAC = 0.857143 -> "0.86" (coincides
    // with the CPI tile's own "0.86" this fixture — hence the scoped query, not a bare getByText).
    tcpiTile = screen.getByText('TCPI (EAC)').closest('[data-state]') as HTMLElement
    expect(within(tcpiTile).getByText('0.86')).toBeInTheDocument()
  })

  it('SPI/CPI tones flip correctly between the unfavourable and favourable fixtures', () => {
    const { rerender } = render(
      <EvmMetricsGrid evm={fixtureA} selectedVariantResult={findVariant(fixtureA, 'CpiBased')} />,
    )
    expect(screen.getByText('0.75').className).toMatch(/text-danger/) // SPI 0.75 < 1

    const cpiTile = screen.getByText('CPI').closest('[data-state]') as HTMLElement
    expect(within(cpiTile).getByText('0.86').className).toMatch(/text-danger/) // CPI 0.857 < 1

    rerender(<EvmMetricsGrid evm={fixtureD} selectedVariantResult={findVariant(fixtureD, 'CpiBased')} />)
    // Fixture D: SPI = CPI = 1.10 (both >= 1, favourable) -> both tiles render "1.10" in success tone.
    const spiValues = screen.getAllByText('1.10')
    expect(spiValues).toHaveLength(2)
    for (const el of spiValues) {
      expect(el.className).toMatch(/text-success/)
    }
  })
})
