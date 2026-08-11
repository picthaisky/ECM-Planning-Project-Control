import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import { StatusPill } from './StatusPill'
import { resolveStatusTone } from './statusTone'

describe('StatusPill', () => {
  it('happy path: renders the label with an explicit tone', () => {
    render(<StatusPill label="Closed" tone="success" />)
    expect(screen.getByText('Closed').className).toMatch(/text-success/)
  })

  it('derives tone from status text when tone is not given (Open -> danger)', () => {
    render(<StatusPill label="เปิด" status="Open" />)
    expect(screen.getByText('เปิด').className).toMatch(/text-danger/)
  })

  it('derives tone from status text (Doing -> warning, Approved -> success, Rejected -> danger)', () => {
    expect(resolveStatusTone('Doing')).toBe('warning')
    expect(resolveStatusTone('Approved')).toBe('success')
    expect(resolveStatusTone('Rejected')).toBe('danger')
    expect(resolveStatusTone('Pending')).toBe('warning')
  })

  it('derives tone for PaymentCertificateStatus (S9-FE-01/02)', () => {
    expect(resolveStatusTone('PendingApproval')).toBe('warning')
    expect(resolveStatusTone('Certified')).toBe('success')
    expect(resolveStatusTone('Paid')).toBe('success')
    // NotDue/Draft/Cancelled fall back to the default neutral tone (no explicit table entry).
    expect(resolveStatusTone('NotDue')).toBe('neutral')
    expect(resolveStatusTone('Cancelled')).toBe('neutral')
  })

  it('unknown status falls back to the neutral tone', () => {
    render(<StatusPill label="Draft" status="draft" />)
    expect(screen.getByText('Draft').className).toMatch(/text-text-muted/)
  })

  it('empty state: renders an em dash placeholder when the label is blank', () => {
    render(<StatusPill label="" />)
    expect(screen.getByText('—')).toBeInTheDocument()
  })
})
