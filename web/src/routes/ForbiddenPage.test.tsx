import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { ForbiddenPage } from './ForbiddenPage'

describe('ForbiddenPage', () => {
  it('renders an actual 403 alert state with Thai copy, not a blank page', () => {
    render(<ForbiddenPage />)

    const alert = screen.getByRole('alert')
    expect(alert).toHaveTextContent('403')
    expect(alert).toHaveTextContent('ไม่มีสิทธิ์เข้าถึงหน้านี้')
  })
})
