import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { EacVariantSelect } from './EacVariantSelect'
import type { EacVariantResponseDto } from '../types'

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

const allFiveComputable: EacVariantResponseDto[] = [
  variant({ variant: 'CpiBased', eac: '1166666.67', computable: true }),
  variant({ variant: 'Atypical', eac: '1050000.00', computable: true }),
  variant({ variant: 'CpiSpiBased', eac: '1438888.89', computable: true }),
  variant({ variant: 'BottomUpEtc', computable: false, reason: 'ManualEtcNotSet' }),
  variant({ variant: 'CustomPf', computable: false, reason: 'CustomPfNotSet' }),
]

// evm-formulas.md A4/A5 — both advanced inputs configured, so BottomUpEtc/CustomPf are computable.
const allFiveConfigured: EacVariantResponseDto[] = [
  variant({ variant: 'CpiBased', eac: '1166666.67', computable: true }),
  variant({ variant: 'Atypical', eac: '1050000.00', computable: true }),
  variant({ variant: 'CpiSpiBased', eac: '1438888.89', computable: true }),
  variant({ variant: 'BottomUpEtc', eac: '1110000.00', etc: '760000.00', computable: true }),
  variant({ variant: 'CustomPf', eac: '1190000.00', etc: '840000.00', performanceFactor: '1.200000', computable: true }),
]

function renderSelect(props: Partial<Parameters<typeof EacVariantSelect>[0]> = {}) {
  return render(
    <MemoryRouter>
      <EacVariantSelect
        variants={allFiveComputable}
        selected="CpiBased"
        onChange={vi.fn()}
        {...props}
      />
    </MemoryRouter>,
  )
}

describe('EacVariantSelect', () => {
  it('S14-FE-02 DoD: shows all 5 engine variants (ADR-0007), not only the 3 index-based ones', () => {
    renderSelect()

    const radios = screen.getAllByRole('radio')
    expect(radios).toHaveLength(5)
    expect(screen.getByText('CPI-Based')).toBeInTheDocument()
    expect(screen.getByText('Atypical')).toBeInTheDocument()
    expect(screen.getByText('CPI × SPI')).toBeInTheDocument()
    expect(screen.getByText('Bottom-Up ETC')).toBeInTheDocument()
    expect(screen.getByText('Custom PF')).toBeInTheDocument()
  })

  it('shows each computable option\'s own EAC preview value, formatted with 2 decimals and thousand separators', () => {
    renderSelect()
    expect(screen.getByText('EAC 1,166,666.67 บาท')).toBeInTheDocument()
    expect(screen.getByText('EAC 1,050,000.00 บาท')).toBeInTheDocument()
    expect(screen.getByText('EAC 1,438,888.89 บาท')).toBeInTheDocument()
  })

  it('renders "—" (never 0 or blank) for a non-computable variant\'s preview', () => {
    const allNull: EacVariantResponseDto[] = [
      variant({ variant: 'CpiBased', computable: false, reason: 'NoActualCost' }),
      variant({ variant: 'Atypical', computable: false, reason: 'NoActualCost' }),
      variant({ variant: 'CpiSpiBased', computable: false, reason: 'NoActualCost' }),
      variant({ variant: 'BottomUpEtc', eac: '1110000.00', computable: true }),
      variant({ variant: 'CustomPf', eac: '1190000.00', computable: true }),
    ]
    renderSelect({ variants: allNull })
    expect(screen.getAllByText('EAC —')).toHaveLength(3)
  })

  it('marks the currently selected variant\'s radio as checked', () => {
    renderSelect({ selected: 'Atypical' })
    expect(screen.getByRole('radio', { name: /Atypical/ })).toBeChecked()
    expect(screen.getByRole('radio', { name: /CPI-Based/ })).not.toBeChecked()
  })

  it('calls onChange with the picked variant — a local selection, not a form submit', async () => {
    const user = userEvent.setup()
    const onChange = vi.fn()
    renderSelect({ onChange })

    await user.click(screen.getByRole('radio', { name: /CPI × SPI/ }))

    expect(onChange).toHaveBeenCalledWith('CpiSpiBased')
    expect(onChange).toHaveBeenCalledTimes(1)
  })

  it('disables every option when the whole fieldset is disabled', () => {
    renderSelect({ disabled: true })
    for (const radio of screen.getAllByRole('radio')) {
      expect(radio).toBeDisabled()
    }
  })

  describe('S14-FE-02 DoD: BottomUpEtc/CustomPf disabled-with-explanation when their input is unset', () => {
    it('disables only BottomUpEtc/CustomPf (not the 3 index-based variants) when their inputs are unset', () => {
      renderSelect()

      expect(screen.getByRole('radio', { name: /CPI-Based/ })).not.toBeDisabled()
      expect(screen.getByRole('radio', { name: /Atypical/ })).not.toBeDisabled()
      expect(screen.getByRole('radio', { name: /CPI × SPI/ })).not.toBeDisabled()
      expect(screen.getByRole('radio', { name: /Bottom-Up ETC/ })).toBeDisabled()
      expect(screen.getByRole('radio', { name: /Custom PF/ })).toBeDisabled()
    })

    it('shows the specific Thai reason under each disabled option, never a bare disabled control', () => {
      renderSelect()

      expect(screen.getByText(/ยังไม่ได้กรอกประมาณการงานที่เหลือ \(Bottom-Up ETC\)/)).toBeInTheDocument()
      expect(screen.getByText(/ยังไม่ได้กำหนดตัวคูณผลการดำเนินงานเอง/)).toBeInTheDocument()
    })

    it('the explanation links to the Project Info screen — the actual place to fix it, not a dead end', () => {
      renderSelect({ projectId: 'project-1' })

      const links = screen.getAllByRole('link', { name: 'ไปกรอกที่หน้าข้อมูลโครงการ →' })
      expect(links).toHaveLength(2) // one under BottomUpEtc, one under CustomPf
      for (const link of links) {
        expect(link).toHaveAttribute('href', '/app/project-1/info')
      }
    })

    it('omits the link (but keeps the disabled state + reason text) when no projectId is supplied', () => {
      renderSelect({ projectId: undefined })

      expect(screen.queryByRole('link')).not.toBeInTheDocument()
      expect(screen.getByRole('radio', { name: /Bottom-Up ETC/ })).toBeDisabled()
    })

    it('once both inputs are configured (A4/A5), both options become selectable with their real EAC preview', () => {
      renderSelect({ variants: allFiveConfigured })

      expect(screen.getByRole('radio', { name: /Bottom-Up ETC/ })).not.toBeDisabled()
      expect(screen.getByRole('radio', { name: /Custom PF/ })).not.toBeDisabled()
      // Fixture A4/A5 (evm-formulas.md).
      expect(screen.getByText('EAC 1,110,000.00 บาท')).toBeInTheDocument()
      expect(screen.getByText('EAC 1,190,000.00 บาท')).toBeInTheDocument()
    })

    it('a variant disabled for a different reason (e.g. NotStarted, which is not the specific missing-input reason) is not treated as "input missing" — no fabricated link', () => {
      const notStarted: EacVariantResponseDto[] = [
        variant({ variant: 'CpiBased', computable: false, reason: 'NotStarted' }),
        variant({ variant: 'Atypical', computable: false, reason: 'NotStarted' }),
        variant({ variant: 'CpiSpiBased', computable: false, reason: 'NotStarted' }),
        variant({ variant: 'BottomUpEtc', computable: false, reason: 'NotStarted' }),
        variant({ variant: 'CustomPf', computable: false, reason: 'NotStarted' }),
      ]
      renderSelect({ variants: notStarted, projectId: 'project-1' })

      // Still enabled (input existence, not "computable right now", is the DoD's actual gate) and
      // shows the ordinary "—" preview rather than the missing-input explanation/link.
      expect(screen.getByRole('radio', { name: /Bottom-Up ETC/ })).not.toBeDisabled()
      expect(screen.getByRole('radio', { name: /Custom PF/ })).not.toBeDisabled()
      expect(screen.queryByRole('link')).not.toBeInTheDocument()
    })
  })
})
