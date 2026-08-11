import { cloneElement, isValidElement, useId, useMemo, useState } from 'react'
import type { FormEvent, ReactElement, ReactNode } from 'react'
import { Button, Modal } from '../../../components'
import { formatMoney } from '../../../utils/format'
import type { CreateVariationOrderPayload, UpdateVariationOrderContentPayload, VariationOrderDto } from '../types'

interface ScopeItemRow {
  activityId: string
  budgetCostDelta: string
  note: string
}

interface FormValues {
  voNumber: string
  description: string
  justification: string
  amount: string
  timeImpactDays: string
  scopeItems: ScopeItemRow[]
}

function emptyRow(): ScopeItemRow {
  return { activityId: '', budgetCostDelta: '', note: '' }
}

function fromVo(vo: VariationOrderDto): FormValues {
  return {
    voNumber: vo.voNumber,
    description: vo.description ?? '',
    justification: vo.justification ?? '',
    amount: vo.amount,
    timeImpactDays: String(vo.timeImpactDays),
    scopeItems: vo.scopeItems.length
      ? vo.scopeItems.map((item) => ({
          activityId: item.activityId,
          budgetCostDelta: item.budgetCostDelta,
          note: item.note ?? '',
        }))
      : [emptyRow()],
  }
}

const inputClass =
  'mt-1 w-full rounded-card border border-border px-3 py-2 text-sm text-text focus:border-navy focus:outline-none'

function Field({
  label,
  error,
  children,
  fullWidth,
}: {
  label: string
  error?: string
  children: ReactNode
  fullWidth?: boolean
}) {
  const inputId = useId()
  const field = isValidElement(children)
    ? cloneElement(children as ReactElement<{ id?: string }>, { id: inputId })
    : children

  return (
    <div className={fullWidth ? 'col-span-2' : undefined}>
      <label htmlFor={inputId} className="block text-[11px] text-text-faint">
        {label}
      </label>
      {field}
      {error && (
        <p role="alert" className="mt-1 text-[10.5px] text-danger">
          {error}
        </p>
      )}
    </div>
  )
}

function scopeTotal(scopeItems: ScopeItemRow[]): number {
  return scopeItems.reduce((sum, item) => sum + (Number(item.budgetCostDelta) || 0), 0)
}

type FieldErrors = Partial<Record<'voNumber' | 'amount' | 'scope' | 'empty', string>>

/** Mirrors `CreateVariationOrderCommandValidator`/`UpdateVariationOrderContentCommandValidator` +
 * the aggregate's own `ApplyContent` invariants (domain-rules.md §5.2/§7.3) — client-visible
 * rejection in Thai; the server re-checks independently regardless. */
function validate(values: FormValues, mode: 'create' | 'edit'): FieldErrors {
  const errors: FieldErrors = {}

  if (mode === 'create' && !values.voNumber.trim()) errors.voNumber = 'จำเป็นต้องกรอก'

  const amount = Number(values.amount)
  if (values.amount.trim() === '' || Number.isNaN(amount)) {
    errors.amount = 'จำเป็นต้องกรอก'
  } else if (amount !== Math.round(amount * 100) / 100) {
    errors.amount = 'ทศนิยมได้ไม่เกิน 2 ตำแหน่ง'
  }

  const activeRows = values.scopeItems.filter((row) => row.activityId.trim() || row.budgetCostDelta.trim())
  const total = scopeTotal(activeRows)
  if (!Number.isNaN(amount) && total !== amount) {
    errors.scope = `ผลรวมมูลค่า Scope (${formatMoney(total)}) ต้องเท่ากับมูลค่า VO (${formatMoney(Number.isNaN(amount) ? 0 : amount)}) พอดี`
  }

  const timeImpactDays = Number(values.timeImpactDays) || 0
  if (!Number.isNaN(amount) && amount === 0 && timeImpactDays === 0 && activeRows.length === 0) {
    errors.empty = 'ต้องระบุมูลค่า, ผลกระทบต่อระยะเวลา หรือรายการ Scope อย่างน้อยหนึ่งอย่าง'
  }

  return errors
}

function toScopeItems(values: FormValues) {
  return values.scopeItems
    .filter((row) => row.activityId.trim() || row.budgetCostDelta.trim())
    .map((row) => ({
      activityId: row.activityId.trim(),
      budgetCostDelta: row.budgetCostDelta.trim() === '' ? '0.00' : row.budgetCostDelta.trim(),
      note: row.note.trim() === '' ? null : row.note.trim(),
    }))
}

export interface VoContentModalProps {
  mode: 'create' | 'edit'
  isOpen: boolean
  onClose: () => void
  /** The VO being edited (`mode === 'edit'`) — supplies `VoNumber` (immutable, display-only) and
   * the current content to prefill. Unused/omitted in `mode === 'create'`. */
  initialVo?: VariationOrderDto | null
  busy: boolean
  errorMessage: string | null
  onCreate: (payload: CreateVariationOrderPayload) => void
  onUpdate: (id: string, payload: UpdateVariationOrderContentPayload) => void
}

/**
 * Create / re-price form for a Variation Order (`Draft`-only per `SetVariationContent`'s guard,
 * domain-rules.md §2.4) — used both for a brand-new VO and for editing a returned-for-revision one
 * before resubmission (the DoD's "เอกสารที่ถูกตีกลับ ส่งใหม่ได้").
 *
 * **Scope items have no activity picker.** There is no live endpoint enumerating a project's
 * activities today (confirmed: no `ActivitiesController`/`IActivityReader` exists anywhere in
 * `backend/src` — the exact same gap `features/wbs/ProgressUpdatePanel.tsx`'s own `ManualActivityRow`
 * already documents and works around). This form follows that established precedent: a manual
 * Activity ID (GUID) text row rather than a fabricated dropdown — honest about the gap, not
 * blocking, and the real `Δ_scope = Amount` invariant (domain-rules.md §5.2) is still fully
 * enforced client-side (live total, shown below) and re-checked server-side regardless.
 */
export function VoContentModal({
  mode,
  isOpen,
  onClose,
  initialVo,
  busy,
  errorMessage,
  onCreate,
  onUpdate,
}: VoContentModalProps) {
  const [values, setValues] = useState<FormValues>(() =>
    initialVo ? fromVo(initialVo) : { voNumber: '', description: '', justification: '', amount: '', timeImpactDays: '0', scopeItems: [emptyRow()] },
  )
  const [errors, setErrors] = useState<FieldErrors>({})

  function set<K extends keyof FormValues>(key: K, value: FormValues[K]) {
    setValues((prev) => ({ ...prev, [key]: value }))
  }

  function setRow(index: number, patch: Partial<ScopeItemRow>) {
    setValues((prev) => ({
      ...prev,
      scopeItems: prev.scopeItems.map((row, i) => (i === index ? { ...row, ...patch } : row)),
    }))
  }

  function addRow() {
    setValues((prev) => ({ ...prev, scopeItems: [...prev.scopeItems, emptyRow()] }))
  }

  function removeRow(index: number) {
    setValues((prev) => ({ ...prev, scopeItems: prev.scopeItems.filter((_, i) => i !== index) }))
  }

  const liveTotal = useMemo(() => scopeTotal(values.scopeItems), [values.scopeItems])
  const liveAmount = Number(values.amount) || 0
  const scopeBalanced = liveTotal === liveAmount

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const nextErrors = validate(values, mode)
    setErrors(nextErrors)
    if (Object.keys(nextErrors).length > 0) return

    const scopeItems = toScopeItems(values)
    const amount = values.amount.trim() === '' ? '0.00' : values.amount.trim()

    if (mode === 'create') {
      onCreate({
        voNumber: values.voNumber.trim(),
        description: values.description.trim() === '' ? null : values.description.trim(),
        justification: values.justification.trim() === '' ? null : values.justification.trim(),
        amount,
        timeImpactDays: Number(values.timeImpactDays) || 0,
        scopeItems,
      })
    } else if (initialVo) {
      onUpdate(initialVo.id, {
        description: values.description.trim() === '' ? null : values.description.trim(),
        justification: values.justification.trim() === '' ? null : values.justification.trim(),
        amount,
        timeImpactDays: Number(values.timeImpactDays) || 0,
        scopeItems,
      })
    }
  }

  return (
    <Modal isOpen={isOpen} onClose={onClose} title={mode === 'create' ? 'สร้าง Variation Order' : `แก้ไข ${initialVo?.voNumber ?? ''}`} className="max-w-lg">
      <form onSubmit={handleSubmit} noValidate aria-label={mode === 'create' ? 'สร้าง Variation Order' : 'แก้ไข Variation Order'}>
        <div className="grid grid-cols-2 gap-3 text-[12.5px]">
          {mode === 'create' ? (
            <Field label="เลขที่ VO" error={errors.voNumber} fullWidth>
              <input className={inputClass} value={values.voNumber} onChange={(e) => set('voNumber', e.target.value)} />
            </Field>
          ) : (
            <Field label="เลขที่ VO" fullWidth>
              <input className={inputClass} value={values.voNumber} disabled />
            </Field>
          )}

          <Field label="รายละเอียด" fullWidth>
            <textarea
              className={inputClass}
              rows={2}
              value={values.description}
              onChange={(e) => set('description', e.target.value)}
            />
          </Field>

          <Field label="เหตุผล / ที่มา (Justification)" fullWidth>
            <textarea
              className={inputClass}
              rows={2}
              value={values.justification}
              onChange={(e) => set('justification', e.target.value)}
            />
          </Field>

          <Field label="มูลค่า VO (บาท) — บวก = งานเพิ่ม, ลบ = งานลด" error={errors.amount}>
            <input
              type="number"
              step="0.01"
              className={inputClass}
              value={values.amount}
              onChange={(e) => set('amount', e.target.value)}
            />
          </Field>
          <Field label="ผลกระทบต่อระยะเวลา (วัน)">
            <input
              type="number"
              step="1"
              className={inputClass}
              value={values.timeImpactDays}
              onChange={(e) => set('timeImpactDays', e.target.value)}
            />
          </Field>
        </div>

        <div className="col-span-2 mt-3 rounded-card border border-border bg-bg p-3">
          <div className="flex items-center justify-between">
            <p className="text-[11px] font-semibold text-navy">รายการปรับงบประมาณ (Scope)</p>
            <Button type="button" size="sm" variant="secondary" onClick={addRow}>
              + เพิ่มรายการ
            </Button>
          </div>

          <div className="mt-2 flex flex-col gap-2">
            {values.scopeItems.map((row, index) => (
              <div key={index} className="grid grid-cols-[1fr_140px_1fr_auto] items-start gap-2">
                <input
                  placeholder="Activity ID"
                  className={inputClass}
                  value={row.activityId}
                  onChange={(e) => setRow(index, { activityId: e.target.value })}
                />
                <input
                  type="number"
                  step="0.01"
                  placeholder="งบที่ปรับ (บาท)"
                  className={inputClass}
                  value={row.budgetCostDelta}
                  onChange={(e) => setRow(index, { budgetCostDelta: e.target.value })}
                />
                <input
                  placeholder="หมายเหตุ (ไม่บังคับ)"
                  className={inputClass}
                  value={row.note}
                  onChange={(e) => setRow(index, { note: e.target.value })}
                />
                <Button
                  type="button"
                  size="sm"
                  variant="secondary"
                  className="mt-1"
                  onClick={() => removeRow(index)}
                  disabled={values.scopeItems.length <= 1}
                >
                  ลบ
                </Button>
              </div>
            ))}
          </div>

          <p className={`mt-2 text-[11px] ${scopeBalanced ? 'text-success' : 'text-warning-text'}`}>
            ผลรวม Scope: {formatMoney(liveTotal)} บาท {scopeBalanced ? '✓ ตรงกับมูลค่า VO' : `(ต้องเท่ากับ ${formatMoney(liveAmount)} บาท พอดี)`}
          </p>
          {errors.scope && (
            <p role="alert" className="mt-1 text-[10.5px] text-danger">
              {errors.scope}
            </p>
          )}
        </div>

        {errors.empty && (
          <p role="alert" className="mt-3 text-[10.5px] text-danger">
            {errors.empty}
          </p>
        )}

        {errorMessage && (
          <p role="alert" className="mt-3 text-xs text-danger">
            {errorMessage}
          </p>
        )}

        <div className="mt-5 flex justify-end gap-2">
          <Button type="button" variant="secondary" size="sm" onClick={onClose} disabled={busy}>
            ยกเลิก
          </Button>
          <Button type="submit" size="sm" loading={busy}>
            บันทึก
          </Button>
        </div>
      </form>
    </Modal>
  )
}
