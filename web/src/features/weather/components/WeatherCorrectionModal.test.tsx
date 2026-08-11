import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { WeatherCorrectionModal } from './WeatherCorrectionModal'
import type { WeatherLogDto } from '../types'

const target: WeatherLogDto = {
  id: 'log-1',
  projectId: 'project-1',
  logDate: '2026-07-08T00:00:00Z',
  condition: 'HeavyRain',
  conditionNote: null,
  rainfallMm: '61.00',
  impact: 'FullStoppage',
  impactNote: 'หยุดงานภายนอกทั้งวัน',
  hoursLost: '8.00',
  workStoppage: true,
  entryKind: 'Original',
  correctsWeatherLogId: null,
  correctionReason: null,
  affectedActivityIds: ['activity-1'],
  recordedByUserId: 'user-1',
  recordedAt: '2026-07-08T09:00:00Z',
}

describe('WeatherCorrectionModal', () => {
  function renderModal(onSubmit = vi.fn()) {
    render(<WeatherCorrectionModal isOpen target={target} onClose={vi.fn()} onSubmit={onSubmit} busy={false} errorMessage={null} />)
    return { onSubmit }
  }

  it('is equally irreversible — warns before this correction/retraction is confirmed too', () => {
    renderModal()
    expect(screen.getByText('รายการแก้ไข/เพิกถอนนี้ก็ไม่สามารถแก้ไขหรือลบได้เช่นกัน')).toBeInTheDocument()
  })

  it('pre-fills the form from the target entry (a correction replaces, so it starts from current values)', () => {
    renderModal()
    expect(screen.getByLabelText('ปริมาณฝน (มม. ใน 24 ชม.) — ไม่บังคับ')).toHaveValue(61)
    expect(screen.getByLabelText(/ชั่วโมงที่หยุดงาน/)).toHaveValue(8)
  })

  it('the confirm button is disabled until acknowledged AND a reason is given', async () => {
    renderModal()
    const confirmButton = screen.getByRole('button', { name: /ยืนยันบันทึกการแก้ไขแบบถาวร/ })
    expect(confirmButton).toBeDisabled()

    await userEvent.click(screen.getByRole('checkbox'))
    expect(confirmButton).toBeDisabled() // still no reason

    await userEvent.type(screen.getByLabelText(/เหตุผลที่แก้ไข/), 'พิมพ์ผิด')
    expect(confirmButton).toBeEnabled()
  })

  it('submits a Correction with the edited field and the original chain-tail id, reason included', async () => {
    const { onSubmit } = renderModal()

    const hoursInput = screen.getByLabelText(/ชั่วโมงที่หยุดงาน/)
    await userEvent.clear(hoursInput)
    await userEvent.type(hoursInput, '3')
    await userEvent.type(screen.getByLabelText(/เหตุผลที่แก้ไข/), 'ตรวจใบบันทึกกะแล้ว หยุดจริง 3 ชั่วโมง')
    await userEvent.click(screen.getByRole('checkbox'))
    await userEvent.click(screen.getByRole('button', { name: /ยืนยันบันทึกการแก้ไขแบบถาวร/ }))

    expect(onSubmit).toHaveBeenCalledTimes(1)
    const [logId, payload] = onSubmit.mock.calls[0]
    expect(logId).toBe('log-1')
    expect(payload.entryKind).toBe('Correction')
    expect(payload.correctionReason).toBe('ตรวจใบบันทึกกะแล้ว หยุดจริง 3 ชั่วโมง')
    expect(payload.hoursLost).toBe('3')
    // Untouched fields still round-trip from the target.
    expect(payload.condition).toBe('HeavyRain')
    expect(payload.affectedActivityIds).toEqual(['activity-1'])
  })

  it('choosing Retraction disables the data fields and submits entryKind=Retraction', async () => {
    const { onSubmit } = renderModal()

    await userEvent.click(screen.getByRole('radio', { name: /เพิกถอนรายการทั้งหมด/ }))
    expect(screen.getByLabelText(/ชั่วโมงที่หยุดงาน/)).toBeDisabled()

    await userEvent.type(screen.getByLabelText(/เหตุผลที่แก้ไข/), 'บันทึกผิดวัน')
    await userEvent.click(screen.getByRole('checkbox'))
    await userEvent.click(screen.getByRole('button', { name: /ยืนยันเพิกถอนแบบถาวร/ }))

    expect(onSubmit).toHaveBeenCalledTimes(1)
    const [, payload] = onSubmit.mock.calls[0]
    expect(payload.entryKind).toBe('Retraction')
    expect(payload.correctionReason).toBe('บันทึกผิดวัน')
  })

  it('reason cannot be whitespace-only', async () => {
    const { onSubmit } = renderModal()
    await userEvent.type(screen.getByLabelText(/เหตุผลที่แก้ไข/), '   ')
    await userEvent.click(screen.getByRole('checkbox'))
    expect(screen.getByRole('button', { name: /ยืนยันบันทึกการแก้ไขแบบถาวร/ })).toBeDisabled()
    expect(onSubmit).not.toHaveBeenCalled()
  })
})
