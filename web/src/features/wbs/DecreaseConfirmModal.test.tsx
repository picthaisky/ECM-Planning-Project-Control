import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { DecreaseConfirmModal } from './DecreaseConfirmModal'
import type { ProgressRow } from './useBatchProgressForm'

/**
 * S4-QA-03 (docs/10 §6): direct component coverage for the "ยืนยันการปรับลดความคืบหน้า" gate itself.
 * Until now this dialog had zero component-level tests of its own — its behaviour was only ever
 * exercised indirectly through `WbsPage.test.tsx` (full page integration) and
 * `useBatchProgressForm.test.ts` (hook logic only, no rendered dialog at all). Both remain valid,
 * but neither proves the *component* renders the right affected rows/copy or that Cancel/Confirm
 * wire to the right callbacks in isolation from the rest of the page.
 */

function makeRow(overrides: Partial<ProgressRow> & { activityId: string }): ProgressRow {
  return {
    activityCode: null,
    name: null,
    currentProgressPercentage: null,
    newProgressPercentage: '',
    actualQuantity: '',
    ...overrides,
  }
}

describe('DecreaseConfirmModal', () => {
  it('renders nothing when isOpen is false — the gate must not appear until attemptSubmit opens it', () => {
    render(
      <DecreaseConfirmModal
        isOpen={false}
        rows={[makeRow({ activityId: 'a1', currentProgressPercentage: '40.00', newProgressPercentage: '20' })]}
        onCancel={vi.fn()}
        onConfirm={vi.fn()}
        confirming={false}
      />,
    )

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(screen.queryByText(/ยืนยันการปรับลดความคืบหน้า/)).not.toBeInTheDocument()
  })

  it('lists every affected row with its current → new percentage', () => {
    render(
      <DecreaseConfirmModal
        isOpen
        rows={[
          makeRow({ activityId: 'a1', activityCode: 'ACT-100', currentProgressPercentage: '40.00', newProgressPercentage: '20' }),
          makeRow({ activityId: 'a2', activityCode: 'ACT-200', currentProgressPercentage: '60.00', newProgressPercentage: '55' }),
        ]}
        onCancel={vi.fn()}
        onConfirm={vi.fn()}
        confirming={false}
      />,
    )

    expect(screen.getByRole('heading', { name: 'ยืนยันการปรับลดความคืบหน้า' })).toBeInTheDocument()
    expect(screen.getByText('ACT-100')).toBeInTheDocument()
    expect(screen.getByText('40.00% → 20.00%')).toBeInTheDocument()
    expect(screen.getByText('ACT-200')).toBeInTheDocument()
    expect(screen.getByText('60.00% → 55.00%')).toBeInTheDocument()
  })

  it('does not call onConfirm just by being rendered/open — only an explicit click submits the decrease', () => {
    const onConfirm = vi.fn()
    render(
      <DecreaseConfirmModal
        isOpen
        rows={[makeRow({ activityId: 'a1', currentProgressPercentage: '40.00', newProgressPercentage: '20' })]}
        onCancel={vi.fn()}
        onConfirm={onConfirm}
        confirming={false}
      />,
    )

    expect(onConfirm).not.toHaveBeenCalled()
  })

  it('clicking "ยกเลิก" calls onCancel and never onConfirm', async () => {
    const onCancel = vi.fn()
    const onConfirm = vi.fn()
    const user = userEvent.setup()
    render(
      <DecreaseConfirmModal
        isOpen
        rows={[makeRow({ activityId: 'a1', currentProgressPercentage: '40.00', newProgressPercentage: '20' })]}
        onCancel={onCancel}
        onConfirm={onConfirm}
        confirming={false}
      />,
    )

    await user.click(screen.getByRole('button', { name: 'ยกเลิก' }))

    expect(onCancel).toHaveBeenCalledTimes(1)
    expect(onConfirm).not.toHaveBeenCalled()
  })

  it('clicking "ยืนยันการปรับลด" calls onConfirm and never onCancel', async () => {
    const onCancel = vi.fn()
    const onConfirm = vi.fn()
    const user = userEvent.setup()
    render(
      <DecreaseConfirmModal
        isOpen
        rows={[makeRow({ activityId: 'a1', currentProgressPercentage: '40.00', newProgressPercentage: '20' })]}
        onCancel={onCancel}
        onConfirm={onConfirm}
        confirming={false}
      />,
    )

    await user.click(screen.getByRole('button', { name: 'ยืนยันการปรับลด' }))

    expect(onConfirm).toHaveBeenCalledTimes(1)
    expect(onCancel).not.toHaveBeenCalled()
  })

  it('while confirming: the confirm button shows a busy/loading state and Cancel is disabled, so the gate cannot be dismissed or re-submitted mid-flight', () => {
    render(
      <DecreaseConfirmModal
        isOpen
        rows={[makeRow({ activityId: 'a1', currentProgressPercentage: '40.00', newProgressPercentage: '20' })]}
        onCancel={vi.fn()}
        onConfirm={vi.fn()}
        confirming
      />,
    )

    const confirmButton = screen.getByRole('button', { name: 'ยืนยันการปรับลด' })
    const cancelButton = screen.getByRole('button', { name: 'ยกเลิก' })

    expect(confirmButton).toHaveAttribute('aria-busy', 'true')
    expect(confirmButton).toBeDisabled()
    expect(cancelButton).toBeDisabled()
  })
})
