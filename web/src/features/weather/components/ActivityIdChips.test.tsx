import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ActivityIdChips } from './ActivityIdChips'

describe('ActivityIdChips', () => {
  it('rejects a non-GUID value with the shared Thai error copy', async () => {
    render(<ActivityIdChips activityIds={[]} onChange={vi.fn()} />)
    await userEvent.type(screen.getByLabelText(/Activity ID/), 'not-a-guid')
    await userEvent.click(screen.getByRole('button', { name: '+ เพิ่ม' }))
    expect(screen.getByText(/รหัสกิจกรรมต้องอยู่ในรูปแบบ GUID/)).toBeInTheDocument()
  })

  it('adds a valid GUID and calls onChange with the appended list', async () => {
    const onChange = vi.fn()
    render(<ActivityIdChips activityIds={[]} onChange={onChange} />)
    await userEvent.type(screen.getByLabelText(/Activity ID/), '3fa85f64-5717-4562-b3fc-2c963f66afa6')
    await userEvent.click(screen.getByRole('button', { name: '+ เพิ่ม' }))
    expect(onChange).toHaveBeenCalledWith(['3fa85f64-5717-4562-b3fc-2c963f66afa6'])
  })

  it('rejects a duplicate id already in the list', async () => {
    render(<ActivityIdChips activityIds={['3fa85f64-5717-4562-b3fc-2c963f66afa6']} onChange={vi.fn()} />)
    await userEvent.type(screen.getByLabelText(/Activity ID/), '3fa85f64-5717-4562-b3fc-2c963f66afa6')
    await userEvent.click(screen.getByRole('button', { name: '+ เพิ่ม' }))
    expect(screen.getByText('กิจกรรมนี้อยู่ในรายการแล้ว')).toBeInTheDocument()
  })

  it('removes a chip via its remove button', async () => {
    const onChange = vi.fn()
    render(<ActivityIdChips activityIds={['3fa85f64-5717-4562-b3fc-2c963f66afa6']} onChange={onChange} />)
    await userEvent.click(screen.getByRole('button', { name: /ลบกิจกรรม/ }))
    expect(onChange).toHaveBeenCalledWith([])
  })

  it('shows the "not yet evaluable" hint when the list is empty (unattributed is legitimate, per §3.2)', () => {
    render(<ActivityIdChips activityIds={[]} onChange={vi.fn()} />)
    expect(screen.getByText(/จะไม่ถูกนับในการประเมิน EOT จนกว่าจะระบุกิจกรรม/)).toBeInTheDocument()
  })

  it('when disabled, hides the remove control and the add form', () => {
    render(<ActivityIdChips activityIds={['3fa85f64-5717-4562-b3fc-2c963f66afa6']} onChange={vi.fn()} disabled />)
    expect(screen.getByLabelText(/Activity ID/)).toBeDisabled()
    expect(screen.queryByRole('button', { name: /ลบกิจกรรม/ })).not.toBeInTheDocument()
  })
})
