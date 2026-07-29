import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { ScreenPlaceholder } from './ScreenPlaceholder'

describe('ScreenPlaceholder', () => {
  it('renders the given title and a "coming soon" note rather than a blank/404 page', () => {
    render(<ScreenPlaceholder title="Gantt / CPM" />)

    expect(screen.getByText('Gantt / CPM')).toBeInTheDocument()
    expect(screen.getByText('หน้านี้จะพร้อมใช้งานในสปรินต์ถัดไป')).toBeInTheDocument()
  })
})
