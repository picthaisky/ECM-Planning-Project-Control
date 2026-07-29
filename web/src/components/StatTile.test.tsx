import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import { StatTile } from './StatTile'

describe('StatTile', () => {
  it('happy path: renders label, value and caption', () => {
    render(<StatTile label="BAC" value="466.30 MB" caption="Budget at Completion" tone="neutral" />)
    expect(screen.getByText('BAC')).toBeInTheDocument()
    expect(screen.getByText('466.30 MB')).toBeInTheDocument()
    expect(screen.getByText('Budget at Completion')).toBeInTheDocument()
  })

  it('applies the danger tone class for over-budget/behind-schedule metrics', () => {
    render(<StatTile label="CPI" value="0.92" tone="danger" />)
    expect(screen.getByText('0.92').className).toMatch(/text-danger/)
  })

  it('loading state: shows a skeleton and not the value', () => {
    render(<StatTile label="EAC" state="loading" />)
    expect(screen.getByRole('status')).toBeInTheDocument()
    expect(screen.queryByText('—')).not.toBeInTheDocument()
  })

  it('empty state: renders an em dash placeholder when there is no value', () => {
    render(<StatTile label="VAC" />)
    expect(screen.getByText('—')).toBeInTheDocument()
  })

  it('error state: shows the error caption in the danger tone', () => {
    render(<StatTile label="SPI" state="error" errorMessage="ไม่สามารถคำนวณได้" />)
    expect(screen.getByText('ไม่สามารถคำนวณได้')).toBeInTheDocument()
    expect(screen.getByText('ไม่สามารถคำนวณได้').className).toMatch(/text-danger/)
  })
})
