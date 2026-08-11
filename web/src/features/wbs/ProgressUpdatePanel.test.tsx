import 'fake-indexeddb/auto'
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ProgressUpdatePanel } from './ProgressUpdatePanel'
import { useBatchProgressForm } from './useBatchProgressForm'
import * as api from './api'
import { WbsApiError } from './api'
import { useAuthStore } from '../../store/authStore'
import type { ProgressRow } from './useBatchProgressForm'

vi.mock('./api', async () => {
  const actual = await vi.importActual<typeof import('./api')>('./api')
  return { ...actual, batchRecordProgress: vi.fn() }
})

/** S13-FE-01: submission now enqueues into the real (fake-indexeddb-polyfilled) `cmplus-outbox`
 * database before syncing — mirrors `features/photo/usePhotoOutbox.test.ts`'s identical reset
 * helper, needed to isolate the "real hook wired in" tests below under the fixed production
 * database name. */
function resetOutboxDatabase(): Promise<void> {
  return new Promise((resolve) => {
    const request = indexedDB.deleteDatabase('cmplus-outbox')
    request.onsuccess = () => resolve()
    request.onerror = () => resolve()
    request.onblocked = () => resolve()
  })
}

/**
 * S4-QA-03 (docs/10 §6): direct component coverage for the batch-progress grid
 * (`ProgressUpdatePanel`), which — like `DecreaseConfirmModal` — had zero component-level tests
 * before this: `useBatchProgressForm.test.ts` only ever exercises the hook via `renderHook` (no
 * rendered DOM at all), and `WbsPage.test.tsx` only exercises the panel indirectly through the
 * whole page. Isolating the panel directly (real component, a hand-built `form` object matching
 * `useBatchProgressForm`'s real return shape) proves the wiring between the two is actually
 * correct, not merely assumed because the hook and the page both work.
 */

type BatchForm = ReturnType<typeof useBatchProgressForm>

function makeForm(overrides: Partial<BatchForm> = {}): BatchForm {
  return {
    rows: [],
    periodEndDate: '',
    setPeriodEndDate: vi.fn(),
    addActivities: vi.fn(),
    addManualRow: vi.fn().mockReturnValue(null),
    updateRowProgress: vi.fn(),
    updateRowQuantity: vi.fn(),
    removeRow: vi.fn(),
    decreasedRows: [],
    validationError: null,
    pendingConfirmation: false,
    attemptSubmit: vi.fn().mockResolvedValue(true),
    confirmDecreaseAndSubmit: vi.fn().mockResolvedValue(true),
    cancelDecreaseConfirmation: vi.fn(),
    submitState: 'idle',
    submitError: null,
    lastResultCount: null,
    outboxItems: [],
    syncCapability: 'fallback-only',
    syncNow: vi.fn().mockResolvedValue(undefined),
    ...overrides,
  }
}

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

describe('ProgressUpdatePanel', () => {
  it('shows the empty-grid message when there are no rows yet', () => {
    render(<ProgressUpdatePanel form={makeForm()} />)
    expect(screen.getByText(/ยังไม่มีกิจกรรมในชุดอัปเดตนี้/)).toBeInTheDocument()
  })

  it('renders each row with its code/name, current %, and editable new % / quantity inputs', () => {
    const form = makeForm({
      rows: [
        makeRow({
          activityId: 'a1',
          activityCode: 'ACT-100',
          name: 'เทคอนกรีต',
          currentProgressPercentage: '40.00',
          newProgressPercentage: '40.00',
          actualQuantity: '12',
        }),
      ],
    })
    render(<ProgressUpdatePanel form={form} />)

    expect(screen.getByText('ACT-100')).toBeInTheDocument()
    expect(screen.getByText('เทคอนกรีต')).toBeInTheDocument()
    expect(screen.getByText('40.00%')).toBeInTheDocument()
    expect(screen.getByLabelText('% ความคืบหน้าใหม่ของ ACT-100')).toHaveValue(40)
    expect(screen.getByLabelText('ปริมาณงานจริงของ ACT-100')).toHaveValue(12)
  })

  it('editing the new-% input calls form.updateRowProgress with the row id and typed value', async () => {
    const form = makeForm({
      rows: [makeRow({ activityId: 'a1', activityCode: 'ACT-100', newProgressPercentage: '40' })],
    })
    const user = userEvent.setup()
    render(<ProgressUpdatePanel form={form} />)

    await user.clear(screen.getByLabelText('% ความคืบหน้าใหม่ของ ACT-100'))
    await user.type(screen.getByLabelText('% ความคืบหน้าใหม่ของ ACT-100'), '5')

    expect(form.updateRowProgress).toHaveBeenCalledWith('a1', expect.any(String))
  })

  it('clicking the row remove (×) button calls form.removeRow with that row\'s id', async () => {
    const form = makeForm({
      rows: [makeRow({ activityId: 'a1', activityCode: 'ACT-100' })],
    })
    const user = userEvent.setup()
    render(<ProgressUpdatePanel form={form} />)

    await user.click(screen.getByRole('button', { name: 'ลบกิจกรรม ACT-100' }))

    expect(form.removeRow).toHaveBeenCalledWith('a1')
  })

  it('a row whose id is in decreasedRows gets the danger/decreased styling on its % input', () => {
    const form = makeForm({
      rows: [makeRow({ activityId: 'a1', activityCode: 'ACT-100', currentProgressPercentage: '40.00', newProgressPercentage: '20' })],
      decreasedRows: [makeRow({ activityId: 'a1', activityCode: 'ACT-100', currentProgressPercentage: '40.00', newProgressPercentage: '20' })],
    })
    render(<ProgressUpdatePanel form={form} />)

    expect(screen.getByLabelText('% ความคืบหน้าใหม่ของ ACT-100')).toHaveClass('border-danger')
  })

  it('manual add: a well-formed id calls form.addManualRow and clears the input on success', async () => {
    const form = makeForm({ addManualRow: vi.fn().mockReturnValue(null) })
    const user = userEvent.setup()
    render(<ProgressUpdatePanel form={form} />)

    const input = screen.getByLabelText('เพิ่มกิจกรรมด้วยรหัส (Activity ID)')
    await user.type(input, '33333333-3333-3333-3333-333333333333')
    await user.click(screen.getByRole('button', { name: '+ เพิ่ม' }))

    expect(form.addManualRow).toHaveBeenCalledWith('33333333-3333-3333-3333-333333333333')
    expect(input).toHaveValue('')
  })

  it('manual add: an invalid id shows the returned Thai error and does not clear the input', async () => {
    const form = makeForm({ addManualRow: vi.fn().mockReturnValue('รหัสกิจกรรมต้องอยู่ในรูปแบบ GUID เช่น ...') })
    const user = userEvent.setup()
    render(<ProgressUpdatePanel form={form} />)

    const input = screen.getByLabelText('เพิ่มกิจกรรมด้วยรหัส (Activity ID)')
    await user.type(input, 'not-a-guid')
    await user.click(screen.getByRole('button', { name: '+ เพิ่ม' }))

    expect(screen.getByRole('alert')).toHaveTextContent('รหัสกิจกรรมต้องอยู่ในรูปแบบ GUID')
    expect(input).toHaveValue('not-a-guid')
  })

  it('shows the node-activities loading indicator when nodeActivitiesLoading is true', () => {
    render(<ProgressUpdatePanel form={makeForm()} nodeActivitiesLoading />)
    expect(screen.getByText(/กำลังโหลดกิจกรรมจากหมวดที่เลือก/)).toBeInTheDocument()
  })

  it('shows the node-activities error with the manual-add fallback hint', () => {
    render(<ProgressUpdatePanel form={makeForm()} nodeActivitiesError="ดำเนินการไม่สำเร็จ กรุณาลองใหม่อีกครั้ง" />)
    expect(screen.getByText(/ดำเนินการไม่สำเร็จ กรุณาลองใหม่อีกครั้ง/)).toBeInTheDocument()
    expect(screen.getByText(/ยังสามารถเพิ่มกิจกรรมด้วยรหัส/)).toBeInTheDocument()
  })

  it('shows form.validationError and form.submitError as alerts', () => {
    const form = makeForm({
      validationError: 'กรุณาระบุวันที่ของงวดข้อมูล (Period End Date)',
      submitError: 'บันทึกความคืบหน้าไม่สำเร็จ',
    })
    render(<ProgressUpdatePanel form={form} />)

    const alerts = screen.getAllByRole('alert')
    expect(alerts.map((a) => a.textContent)).toEqual(
      expect.arrayContaining([
        expect.stringContaining('กรุณาระบุวันที่ของงวดข้อมูล'),
        expect.stringContaining('บันทึกความคืบหน้าไม่สำเร็จ'),
      ]),
    )
  })

  it('shows a "queued" status message after a submit that cleared the grid (S13-FE-01: enqueued, not yet necessarily synced)', () => {
    const form = makeForm({ rows: [], submitState: 'idle', lastResultCount: 20 })
    render(<ProgressUpdatePanel form={form} />)

    expect(screen.getByRole('status')).toHaveTextContent('คิวไว้แล้ว 20 รายการ (รอซิงค์)')
  })

  it('clicking the submit button calls form.attemptSubmit', async () => {
    const form = makeForm({ rows: [makeRow({ activityId: 'a1', newProgressPercentage: '50' })] })
    const user = userEvent.setup()
    render(<ProgressUpdatePanel form={form} />)

    await user.click(screen.getByRole('button', { name: 'ส่งข้อมูลความคืบหน้า' }))

    expect(form.attemptSubmit).toHaveBeenCalledTimes(1)
  })

  it('shows the submit button in a busy/disabled state while submitState is "submitting"', () => {
    const form = makeForm({ submitState: 'submitting' })
    render(<ProgressUpdatePanel form={form} />)

    const submitButton = screen.getByRole('button', { name: 'ส่งข้อมูลความคืบหน้า' })
    expect(submitButton).toHaveAttribute('aria-busy', 'true')
    expect(submitButton).toBeDisabled()
  })

  describe('the decrease-confirmation gate (wired to DecreaseConfirmModal)', () => {
    it('opens the dialog when form.pendingConfirmation is true, listing form.decreasedRows', () => {
      const decreased = [makeRow({ activityId: 'a1', activityCode: 'ACT-100', currentProgressPercentage: '40.00', newProgressPercentage: '20' })]
      const form = makeForm({ pendingConfirmation: true, decreasedRows: decreased })
      render(<ProgressUpdatePanel form={form} />)

      expect(screen.getByRole('heading', { name: 'ยืนยันการปรับลดความคืบหน้า' })).toBeInTheDocument()
      expect(screen.getByText('ACT-100')).toBeInTheDocument()
    })

    it('does not render the dialog when form.pendingConfirmation is false', () => {
      render(<ProgressUpdatePanel form={makeForm({ pendingConfirmation: false })} />)
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    })

    it('confirming inside the dialog calls form.confirmDecreaseAndSubmit — proving the gate genuinely blocks until this explicit action, not just that the dialog appears', async () => {
      const form = makeForm({
        pendingConfirmation: true,
        decreasedRows: [makeRow({ activityId: 'a1', currentProgressPercentage: '40.00', newProgressPercentage: '20' })],
      })
      const user = userEvent.setup()
      render(<ProgressUpdatePanel form={form} />)

      await user.click(screen.getByRole('button', { name: 'ยืนยันการปรับลด' }))

      expect(form.confirmDecreaseAndSubmit).toHaveBeenCalledTimes(1)
    })

    it('cancelling inside the dialog calls form.cancelDecreaseConfirmation, never confirmDecreaseAndSubmit', async () => {
      const form = makeForm({
        pendingConfirmation: true,
        decreasedRows: [makeRow({ activityId: 'a1', currentProgressPercentage: '40.00', newProgressPercentage: '20' })],
      })
      const user = userEvent.setup()
      render(<ProgressUpdatePanel form={form} />)

      await user.click(screen.getByRole('button', { name: 'ยกเลิก' }))

      expect(form.cancelDecreaseConfirmation).toHaveBeenCalledTimes(1)
      expect(form.confirmDecreaseAndSubmit).not.toHaveBeenCalled()
    })
  })

  describe('virtualization at a large row count (ADR-0004)', () => {
    beforeAll(() => {
      Object.defineProperty(HTMLElement.prototype, 'offsetHeight', { configurable: true, get: () => 520 })
      Object.defineProperty(HTMLElement.prototype, 'offsetWidth', { configurable: true, get: () => 900 })
    })

    afterAll(() => {
      Reflect.deleteProperty(HTMLElement.prototype, 'offsetHeight')
      Reflect.deleteProperty(HTMLElement.prototype, 'offsetWidth')
    })

    it('does NOT produce one DOM row per activity at 600 rows — reusing the same DOM-node-count assertion DataTable.test.tsx established for the WBS tree grid, applied here to confirm (rather than assume) the same guarantee holds for the batch-progress grid', () => {
      const rows = Array.from({ length: 600 }, (_, i) =>
        makeRow({
          activityId: `a${i}`,
          activityCode: `ACT-${i}`,
          newProgressPercentage: '50.00',
        }),
      )
      const form = makeForm({ rows })
      const { container } = render(<ProgressUpdatePanel form={form} />)

      // Each row renders a "% ความคืบหน้าใหม่ของ ACT-n" labelled input - one unique, unambiguous
      // DOM marker per row, independent of any virtualization/windowing the panel may or may not do.
      const renderedRowInputs = container.querySelectorAll('input[aria-label^="% ความคืบหน้าใหม่ของ"]')

      expect(rows.length).toBe(600)
      // ADR-0004 / S4-FE-03 DoD ("500+ แถวใช้ virtualization"): this is the same generous-upper-
      // -bound style DataTable.test.tsx's own virtualization test uses - a virtualized viewport of
      // this size would render on the order of tens of rows, never hundreds.
      expect(renderedRowInputs.length).toBeLessThan(rows.length)
    })
  })

  describe('S13-FE-01: real hook wired in (not a hand-built form object) — always outbox, never a direct call', () => {
    beforeEach(async () => {
      await resetOutboxDatabase()
      vi.mocked(api.batchRecordProgress).mockReset()
      useAuthStore.getState().login({
        accessToken: 'jwt',
        expiresAt: '2027-01-01T00:00:00+07:00',
        userId: 'user-1',
        tenantId: 'tenant-1',
        role: 'PM',
      })
    })

    afterEach(async () => {
      useAuthStore.getState().logout()
      await resetOutboxDatabase()
    })

    function RealPanel() {
      const form = useBatchProgressForm('project-1')
      return <ProgressUpdatePanel form={form} />
    }

    it('submitting enqueues immediately and clears the grid, regardless of what the server will eventually say (ADR-0005: enqueue always happens first)', async () => {
      // Never resolves within this test — proves the grid clears on *enqueue*, not on a server round
      // trip (the old, direct-API behaviour this replaces required a resolved/rejected promise to
      // reach either branch at all).
      vi.mocked(api.batchRecordProgress).mockReturnValue(new Promise(() => {}))
      const user = userEvent.setup()
      render(<RealPanel />)

      await user.type(
        screen.getByLabelText('เพิ่มกิจกรรมด้วยรหัส (Activity ID)'),
        '11111111-1111-1111-1111-111111111111',
      )
      await user.click(screen.getByRole('button', { name: '+ เพิ่ม' }))
      await user.type(screen.getByLabelText('วันที่ของงวดข้อมูล (Period End Date)'), '2026-07-27')
      await user.type(screen.getByLabelText(/% ความคืบหน้าใหม่ของ/), '50')

      await user.click(screen.getByRole('button', { name: 'ส่งข้อมูลความคืบหน้า' }))

      await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent('คิวไว้แล้ว 1 รายการ'))
      expect(screen.queryByText('11111111-1111-1111-1111-111111111111')).not.toBeInTheDocument() // grid cleared
      expect(screen.getByTestId('progress-outbox-item')).toBeInTheDocument() // now visible in the queue instead
    })

    it('a batch that fails to sync (server rejects) surfaces its real Thai error in the offline queue, not as a blocking form error', async () => {
      vi.mocked(api.batchRecordProgress).mockRejectedValueOnce(
        new WbsApiError('พบรหัสกิจกรรมที่ไม่อยู่ในโครงการนี้ กรุณาตรวจสอบรายการอีกครั้ง', 400, 'ProgressUnknownActivity'),
      )
      const user = userEvent.setup()
      render(<RealPanel />)

      await user.type(
        screen.getByLabelText('เพิ่มกิจกรรมด้วยรหัส (Activity ID)'),
        '22222222-2222-2222-2222-222222222222',
      )
      await user.click(screen.getByRole('button', { name: '+ เพิ่ม' }))
      await user.type(screen.getByLabelText('วันที่ของงวดข้อมูล (Period End Date)'), '2026-07-27')
      await user.type(screen.getByLabelText(/% ความคืบหน้าใหม่ของ/), '50')

      await user.click(screen.getByRole('button', { name: 'ส่งข้อมูลความคืบหน้า' }))

      // The batch is queued (and the row-editing grid cleared) well before the API call resolves —
      // there is no `form.submitError`/blocking alert path for this outcome any more.
      await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent('คิวไว้แล้ว 1 รายการ'))
      expect(screen.queryByRole('alert')).not.toBeInTheDocument()

      // Once the (mocked) server rejects it, the real Thai message appears against the queued item.
      await waitFor(() =>
        expect(screen.getByTestId('progress-outbox-item')).toHaveTextContent('พบรหัสกิจกรรมที่ไม่อยู่ในโครงการนี้'),
      )
      expect(screen.getByTestId('progress-outbox-item')).toHaveAttribute('data-outbox-status', 'failed')
    })
  })
})
