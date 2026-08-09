import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ZoomControl } from './ZoomControl'

describe('ZoomControl', () => {
  it('renders all three zoom levels with the active one marked aria-pressed', () => {
    render(<ZoomControl zoom="week" onChange={vi.fn()} />)

    expect(screen.getByRole('button', { name: 'วัน' })).toHaveAttribute('aria-pressed', 'false')
    expect(screen.getByRole('button', { name: 'สัปดาห์' })).toHaveAttribute('aria-pressed', 'true')
    expect(screen.getByRole('button', { name: 'เดือน' })).toHaveAttribute('aria-pressed', 'false')
  })

  it('calls onChange with the clicked zoom level', async () => {
    const onChange = vi.fn()
    const user = userEvent.setup()
    render(<ZoomControl zoom="week" onChange={onChange} />)

    await user.click(screen.getByRole('button', { name: 'เดือน' }))

    expect(onChange).toHaveBeenCalledWith('month')
  })
})
