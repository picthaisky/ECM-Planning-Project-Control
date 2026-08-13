import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { CreateCertificateModal } from './CreateCertificateModal'
import { createPaymentCertificate } from '../api'
import type { PaymentCertificateDto } from '../types'

vi.mock('../api', () => {
  class PaymentApiError extends Error {
    status?: number
    constructor(message: string, status?: number) {
      super(message)
      this.name = 'PaymentApiError'
      this.status = status
    }
  }
  return { createPaymentCertificate: vi.fn(), PaymentApiError }
})

const created = { id: 'cert-9', projectId: 'p1' } as unknown as PaymentCertificateDto

describe('CreateCertificateModal', () => {
  it('submits the certified inputs and upserts the created certificate', async () => {
    vi.mocked(createPaymentCertificate).mockResolvedValueOnce(created)
    const onCreated = vi.fn()
    const onClose = vi.fn()
    render(<CreateCertificateModal projectId="p1" isOpen onClose={onClose} onCreated={onCreated} />)

    await userEvent.clear(screen.getByLabelText('งวดที่ (Milestone No.)'))
    await userEvent.type(screen.getByLabelText('งวดที่ (Milestone No.)'), '2')
    await userEvent.type(screen.getByLabelText('% ที่รับรองสะสมงวดนี้'), '30')
    await userEvent.type(screen.getByLabelText('มูลค่างวดงาน (Milestone Value)'), '20000000')
    await userEvent.click(screen.getByRole('button', { name: 'สร้างใบรับรอง' }))

    expect(createPaymentCertificate).toHaveBeenCalledWith('p1', {
      milestoneNo: 2,
      description: null,
      milestoneValue: 20_000_000,
      thisCumulativeApprovePct: 30,
    })
    expect(onCreated).toHaveBeenCalledWith(created)
    expect(onClose).toHaveBeenCalled()
  })

  it('renders the server error inline and does not close on failure', async () => {
    const { PaymentApiError } = await import('../api')
    vi.mocked(createPaymentCertificate).mockRejectedValueOnce(
      new (PaymentApiError as unknown as typeof Error)('ยังไม่ได้ตั้งค่าอัตรา Retention'),
    )
    const onClose = vi.fn()
    render(<CreateCertificateModal projectId="p1" isOpen onClose={onClose} onCreated={vi.fn()} />)

    await userEvent.type(screen.getByLabelText('% ที่รับรองสะสมงวดนี้'), '30')
    await userEvent.type(screen.getByLabelText('มูลค่างวดงาน (Milestone Value)'), '20000000')
    await userEvent.click(screen.getByRole('button', { name: 'สร้างใบรับรอง' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('ยังไม่ได้ตั้งค่าอัตรา Retention')
    expect(onClose).not.toHaveBeenCalled()
  })
})
