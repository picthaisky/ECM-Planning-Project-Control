import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
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

describe('EacVariantSelect', () => {
  it('DoD: shows exactly 3 options — CpiBased, Atypical, CpiSpiBased — never BottomUpEtc/CustomPf', () => {
    render(<EacVariantSelect variants={allFiveComputable} selected="CpiBased" onChange={vi.fn()} />)

    const radios = screen.getAllByRole('radio')
    expect(radios).toHaveLength(3)
    expect(screen.getByText('CPI-Based')).toBeInTheDocument()
    expect(screen.getByText('Atypical')).toBeInTheDocument()
    expect(screen.getByText('CPI × SPI')).toBeInTheDocument()
    expect(screen.queryByText('Bottom-Up ETC')).not.toBeInTheDocument()
    expect(screen.queryByText('Custom PF')).not.toBeInTheDocument()
  })

  it('shows each option\'s own EAC preview value, formatted with 2 decimals and thousand separators', () => {
    render(<EacVariantSelect variants={allFiveComputable} selected="CpiBased" onChange={vi.fn()} />)
    expect(screen.getByText('EAC 1,166,666.67 บาท')).toBeInTheDocument()
    expect(screen.getByText('EAC 1,050,000.00 บาท')).toBeInTheDocument()
    expect(screen.getByText('EAC 1,438,888.89 บาท')).toBeInTheDocument()
  })

  it('renders "—" (never 0 or blank) for a non-computable variant\'s preview', () => {
    const allNull: EacVariantResponseDto[] = [
      variant({ variant: 'CpiBased', computable: false, reason: 'NoActualCost' }),
      variant({ variant: 'Atypical', computable: false, reason: 'NoActualCost' }),
      variant({ variant: 'CpiSpiBased', computable: false, reason: 'NoActualCost' }),
    ]
    render(<EacVariantSelect variants={allNull} selected="CpiBased" onChange={vi.fn()} />)
    expect(screen.getAllByText('EAC —')).toHaveLength(3)
  })

  it('marks the currently selected variant\'s radio as checked', () => {
    render(<EacVariantSelect variants={allFiveComputable} selected="Atypical" onChange={vi.fn()} />)
    expect(screen.getByRole('radio', { name: /Atypical/ })).toBeChecked()
    expect(screen.getByRole('radio', { name: /CPI-Based/ })).not.toBeChecked()
  })

  it('calls onChange with the picked variant — a local selection, not a form submit', async () => {
    const user = userEvent.setup()
    const onChange = vi.fn()
    render(<EacVariantSelect variants={allFiveComputable} selected="CpiBased" onChange={onChange} />)

    await user.click(screen.getByRole('radio', { name: /CPI × SPI/ }))

    expect(onChange).toHaveBeenCalledWith('CpiSpiBased')
    expect(onChange).toHaveBeenCalledTimes(1)
  })

  it('disables every option when disabled', () => {
    render(<EacVariantSelect variants={allFiveComputable} selected="CpiBased" onChange={vi.fn()} disabled />)
    for (const radio of screen.getAllByRole('radio')) {
      expect(radio).toBeDisabled()
    }
  })
})
