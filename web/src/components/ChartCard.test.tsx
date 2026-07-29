import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import { ChartCard } from './ChartCard'

describe('ChartCard', () => {
  it('happy path: renders title, legend/actions slots and chart children', () => {
    render(
      <ChartCard
        title="S-Curve"
        subtitle="PV/EV/AC/EAC"
        legend={<span>legend</span>}
        actions={<button type="button">Export</button>}
      >
        <svg data-testid="chart-svg" />
      </ChartCard>,
    )
    expect(screen.getByText('S-Curve')).toBeInTheDocument()
    expect(screen.getByText('PV/EV/AC/EAC')).toBeInTheDocument()
    expect(screen.getByText('legend')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Export' })).toBeInTheDocument()
    expect(screen.getByTestId('chart-svg')).toBeInTheDocument()
  })

  it('loading state: shows a loading placeholder instead of children', () => {
    render(
      <ChartCard title="Cash Flow" state="loading">
        <svg data-testid="chart-svg" />
      </ChartCard>,
    )
    expect(screen.getByRole('status')).toBeInTheDocument()
    expect(screen.queryByTestId('chart-svg')).not.toBeInTheDocument()
  })

  it('empty state: shows the empty message instead of children', () => {
    render(
      <ChartCard title="EVM" state="empty" emptyMessage="ยังไม่มีข้อมูลงวดนี้">
        <svg data-testid="chart-svg" />
      </ChartCard>,
    )
    expect(screen.getByText('ยังไม่มีข้อมูลงวดนี้')).toBeInTheDocument()
    expect(screen.queryByTestId('chart-svg')).not.toBeInTheDocument()
  })

  it('error state: shows the error message instead of children', () => {
    render(
      <ChartCard title="EVM" state="error" errorMessage="โหลดกราฟไม่สำเร็จ">
        <svg data-testid="chart-svg" />
      </ChartCard>,
    )
    expect(screen.getByRole('alert')).toHaveTextContent('โหลดกราฟไม่สำเร็จ')
    expect(screen.queryByTestId('chart-svg')).not.toBeInTheDocument()
  })
})
