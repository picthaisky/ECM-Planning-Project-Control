import { cloneElement, isValidElement, useId, useMemo, useState } from 'react'
import type { FormEvent, ReactElement, ReactNode } from 'react'
import { Button } from '../../components'
import {
  NORMAL_RETENTION_CAP_WARNING_PCT,
  calculateRetentionCapAmount,
  isAboveNormalRetentionCapThreshold,
} from './retentionCap'
import type { AdvanceRecoveryMethod, Project, UpdateProjectPayload } from './types'
import { formatMoney } from '../../utils/format'

export interface ProjectEditFormProps {
  project: Project
  saving: boolean
  serverError: string | null
  onCancel: () => void
  onSubmit: (payload: UpdateProjectPayload) => void
}

/** All-string form state — every field is a controlled text/select input; parsed/validated only
 * at submit time (and for the live retention-cap preview), never coerced mid-typing. */
interface FormValues {
  name: string
  code: string
  owner: string
  contractStart: string
  contractFinish: string
  bac: string
  contractValue: string
  retentionRate: string
  advanceRate: string
  retentionUncapped: boolean
  retentionCapPercentage: string
  retentionRelease1Percentage: string
  defectsLiabilityMonths: string
  advanceAmountPaid: string
  advanceRecoveryMethod: AdvanceRecoveryMethod
  advanceRecoveryStartPct: string
  advanceRecoveryRatePct: string
  advanceRecoveryEndPct: string
}

function toDateInputValue(iso: string): string {
  // `DateTimeOffset` ISO strings ("2025-10-01T00:00:00+07:00") -> <input type="date"> ("2025-10-01").
  const parsed = new Date(iso)
  return Number.isNaN(parsed.getTime()) ? '' : parsed.toISOString().slice(0, 10)
}

function toRequestDate(dateInputValue: string): string {
  // Round-trips as UTC midnight — contract dates are calendar dates, not a specific instant;
  // the backend only ever compares them to each other (ContractFinish >= ContractStart).
  return new Date(`${dateInputValue}T00:00:00Z`).toISOString()
}

function fromProject(project: Project): FormValues {
  return {
    name: project.name,
    code: project.code,
    owner: project.owner,
    contractStart: toDateInputValue(project.contractStart),
    contractFinish: toDateInputValue(project.contractFinish),
    bac: project.bac,
    contractValue: project.contractValue,
    retentionRate: project.retentionRate ?? '',
    advanceRate: project.advanceRate ?? '',
    retentionUncapped: project.retentionCapPercentage === null,
    retentionCapPercentage: project.retentionCapPercentage ?? '',
    retentionRelease1Percentage: project.retentionRelease1Percentage,
    defectsLiabilityMonths: project.defectsLiabilityMonths?.toString() ?? '',
    advanceAmountPaid: project.advanceAmountPaid ?? '',
    advanceRecoveryMethod: project.advanceRecoveryMethod,
    advanceRecoveryStartPct: project.advanceRecoveryStartPct ?? '',
    advanceRecoveryRatePct: project.advanceRecoveryRatePct ?? '',
    advanceRecoveryEndPct: project.advanceRecoveryEndPct ?? '',
}
}

type FieldErrors = Partial<Record<keyof FormValues, string>>

const REQUIRED_MESSAGE = 'จำเป็นต้องกรอก'
const RANGE_MESSAGE = 'ต้องอยู่ระหว่าง 0.00 ถึง 100.00'
const NON_NEGATIVE_MESSAGE = 'ต้องไม่ติดลบ'

function parseOptionalPercent(raw: string): number | null {
  if (raw.trim() === '') return null
  return Number(raw)
}

/** Mirrors `UpdateProjectCommandValidator` exactly (0.00-100.00 inclusive for every percent field,
 * ContractFinish >= ContractStart, non-negative money/int) — client-visible rejection in Thai, the
 * "defense-in-depth" pairing the S4-FE-02 DoD requires (server re-checks independently). */
function validate(values: FormValues): FieldErrors {
  const errors: FieldErrors = {}

  if (!values.name.trim()) errors.name = REQUIRED_MESSAGE
  if (!values.code.trim()) errors.code = REQUIRED_MESSAGE
  if (!values.owner.trim()) errors.owner = REQUIRED_MESSAGE

  if (!values.contractStart) errors.contractStart = REQUIRED_MESSAGE
  if (!values.contractFinish) errors.contractFinish = REQUIRED_MESSAGE
  if (
    values.contractStart &&
    values.contractFinish &&
    values.contractFinish < values.contractStart
  ) {
    errors.contractFinish = 'วันที่สิ้นสุดสัญญาต้องไม่ก่อนวันที่เริ่มสัญญา'
  }

  if (values.bac.trim() === '' || Number(values.bac) < 0) errors.bac = NON_NEGATIVE_MESSAGE
  if (values.contractValue.trim() === '' || Number(values.contractValue) < 0) {
    errors.contractValue = NON_NEGATIVE_MESSAGE
  }

  const percentFields: Array<[keyof FormValues, string]> = [
    ['retentionRate', values.retentionRate],
    ['advanceRate', values.advanceRate],
    ['retentionRelease1Percentage', values.retentionRelease1Percentage],
    ['advanceRecoveryStartPct', values.advanceRecoveryStartPct],
    ['advanceRecoveryRatePct', values.advanceRecoveryRatePct],
    ['advanceRecoveryEndPct', values.advanceRecoveryEndPct],
  ]
  for (const [field, raw] of percentFields) {
    if (raw.trim() === '') continue
    const numeric = Number(raw)
    if (Number.isNaN(numeric) || numeric < 0 || numeric > 100) errors[field] = RANGE_MESSAGE
  }

  if (!values.retentionUncapped) {
    const numeric = Number(values.retentionCapPercentage)
    if (values.retentionCapPercentage.trim() === '' || Number.isNaN(numeric) || numeric < 0 || numeric > 100) {
      errors.retentionCapPercentage = RANGE_MESSAGE
    }
  }

  if (values.defectsLiabilityMonths.trim() !== '' && Number(values.defectsLiabilityMonths) < 0) {
    errors.defectsLiabilityMonths = NON_NEGATIVE_MESSAGE
  }
  if (values.advanceAmountPaid.trim() !== '' && Number(values.advanceAmountPaid) < 0) {
    errors.advanceAmountPaid = NON_NEGATIVE_MESSAGE
  }

  return errors
}

function toPayload(values: FormValues): UpdateProjectPayload {
  return {
    name: values.name.trim(),
    code: values.code.trim(),
    owner: values.owner.trim(),
    contractStart: toRequestDate(values.contractStart),
    contractFinish: toRequestDate(values.contractFinish),
    bac: values.bac,
    contractValue: values.contractValue,
    retentionRate: values.retentionRate.trim() === '' ? null : values.retentionRate,
    advanceRate: values.advanceRate.trim() === '' ? null : values.advanceRate,
    retentionCapPercentage: values.retentionUncapped ? null : values.retentionCapPercentage,
    retentionRelease1Percentage: values.retentionRelease1Percentage || '50.00',
    defectsLiabilityMonths:
      values.defectsLiabilityMonths.trim() === '' ? null : Number(values.defectsLiabilityMonths),
    advanceAmountPaid: values.advanceAmountPaid.trim() === '' ? null : values.advanceAmountPaid,
    advanceRecoveryMethod: values.advanceRecoveryMethod,
    advanceRecoveryStartPct:
      values.advanceRecoveryStartPct.trim() === '' ? null : values.advanceRecoveryStartPct,
    advanceRecoveryRatePct:
      values.advanceRecoveryRatePct.trim() === '' ? null : values.advanceRecoveryRatePct,
    advanceRecoveryEndPct:
      values.advanceRecoveryEndPct.trim() === '' ? null : values.advanceRecoveryEndPct,
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
  // Every call site passes exactly one <input>/<select> as `children` — clone it once to wire up
  // `id`/`htmlFor` so `getByLabelText` (and real screen readers) associate the label correctly,
  // without every one of the ~20 call sites above having to generate and pass its own id.
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

/**
 * S4-FE-02 project master-data edit form (US-4.3/4.4). Field set matches `UpdateProjectCommand`
 * exactly (`api.ts`/`types.ts`). Client-side validation mirrors `UpdateProjectCommandValidator`
 * (Thai messages); the retention-cap preview is always the **live, computed** amount from the
 * in-progress `contractValue`/`retentionCapPercentage` values (risk R-02 — never a static example).
 */
export function ProjectEditForm({ project, saving, serverError, onCancel, onSubmit }: ProjectEditFormProps) {
  const [values, setValues] = useState<FormValues>(() => fromProject(project))
  const [errors, setErrors] = useState<FieldErrors>({})
  const formId = useId()

  function set<K extends keyof FormValues>(key: K, value: FormValues[K]) {
    setValues((prev) => ({ ...prev, [key]: value }))
  }

  const capPreview = useMemo(() => {
    const contractValue = Number(values.contractValue)
    const capPct = values.retentionUncapped ? null : parseOptionalPercent(values.retentionCapPercentage)
    if (Number.isNaN(contractValue)) return null
    return calculateRetentionCapAmount(contractValue, capPct)
  }, [values.contractValue, values.retentionUncapped, values.retentionCapPercentage])

  const showCapWarning =
    !values.retentionUncapped && isAboveNormalRetentionCapThreshold(Number(values.retentionCapPercentage))

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const nextErrors = validate(values)
    setErrors(nextErrors)
    if (Object.keys(nextErrors).length > 0) return
    onSubmit(toPayload(values))
  }

  const isThresholdBanded = values.advanceRecoveryMethod === 'ThresholdBanded'

  return (
    <form onSubmit={handleSubmit} noValidate aria-label="แก้ไขข้อมูลโครงการ">
      <div className="grid grid-cols-2 gap-3 text-[12.5px]">
        <Field label="ชื่อโครงการ" error={errors.name} fullWidth>
          <input
            className={inputClass}
            value={values.name}
            onChange={(e) => set('name', e.target.value)}
          />
        </Field>
        <Field label="รหัสโครงการ" error={errors.code}>
          <input
            className={inputClass}
            value={values.code}
            onChange={(e) => set('code', e.target.value)}
          />
        </Field>
        <Field label="เจ้าของโครงการ" error={errors.owner}>
          <input
            className={inputClass}
            value={values.owner}
            onChange={(e) => set('owner', e.target.value)}
          />
        </Field>
        <Field label="วันเริ่มสัญญา" error={errors.contractStart}>
          <input
            type="date"
            className={inputClass}
            value={values.contractStart}
            onChange={(e) => set('contractStart', e.target.value)}
          />
        </Field>
        <Field label="วันสิ้นสุดสัญญา" error={errors.contractFinish}>
          <input
            type="date"
            className={inputClass}
            value={values.contractFinish}
            onChange={(e) => set('contractFinish', e.target.value)}
          />
        </Field>
        <Field label="มูลค่าสัญญา (BAC ตั้งต้น)" error={errors.bac}>
          <input
            type="number"
            step="0.01"
            min="0"
            className={inputClass}
            value={values.bac}
            onChange={(e) => set('bac', e.target.value)}
          />
        </Field>
        <Field label="มูลค่าสัญญา (Contract Value)" error={errors.contractValue}>
          <input
            type="number"
            step="0.01"
            min="0"
            className={inputClass}
            value={values.contractValue}
            onChange={(e) => set('contractValue', e.target.value)}
          />
        </Field>
        <Field label="Retention Rate (%)" error={errors.retentionRate}>
          <input
            type="number"
            step="0.01"
            min="0"
            max="100"
            placeholder="ไม่กำหนด"
            className={inputClass}
            value={values.retentionRate}
            onChange={(e) => set('retentionRate', e.target.value)}
          />
        </Field>
        <Field label="Advance Rate (%)" error={errors.advanceRate}>
          <input
            type="number"
            step="0.01"
            min="0"
            max="100"
            placeholder="ไม่กำหนด"
            className={inputClass}
            value={values.advanceRate}
            onChange={(e) => set('advanceRate', e.target.value)}
          />
        </Field>

        <div className="col-span-2 rounded-card border border-border bg-bg p-3">
          <div className="flex items-center gap-2">
            <input
              id={`${formId}-uncapped`}
              type="checkbox"
              checked={values.retentionUncapped}
              onChange={(e) => set('retentionUncapped', e.target.checked)}
            />
            <label htmlFor={`${formId}-uncapped`} className="text-[12px] text-text">
              ไม่มีเพดาน Retention (มาตรฐานไทย)
            </label>
          </div>

          {!values.retentionUncapped && (
            <Field label="Retention Cap (% ของมูลค่าสัญญา)" error={errors.retentionCapPercentage}>
              <input
                type="number"
                step="0.01"
                min="0"
                max="100"
                className={inputClass}
                value={values.retentionCapPercentage}
                onChange={(e) => set('retentionCapPercentage', e.target.value)}
              />
            </Field>
          )}

          <p className="mt-2 text-[11px] text-text-muted">
            เพดาน Retention ที่คำนวณได้ (จากมูลค่าสัญญา):{' '}
            <span className="font-semibold text-navy">
              {capPreview === null ? 'ไม่มีเพดาน' : `${formatMoney(capPreview)} บาท`}
            </span>
          </p>
          {showCapWarning && (
            <p className="mt-1 text-[10.5px] text-warning-text">
              เพดาน Retention สูงกว่าค่าปกติ ({NORMAL_RETENTION_CAP_WARNING_PCT.toFixed(2)}%) —
              กรุณาตรวจสอบเงื่อนไขสัญญาอีกครั้ง (ไม่ใช่ข้อผิดพลาด สามารถบันทึกต่อได้)
            </p>
          )}
        </div>

        <Field label="Retention Release งวดแรก (%)" error={errors.retentionRelease1Percentage}>
          <input
            type="number"
            step="0.01"
            min="0"
            max="100"
            className={inputClass}
            value={values.retentionRelease1Percentage}
            onChange={(e) => set('retentionRelease1Percentage', e.target.value)}
          />
        </Field>
        <Field label="ระยะเวลารับประกันผลงาน (เดือน)" error={errors.defectsLiabilityMonths}>
          <input
            type="number"
            step="1"
            min="0"
            placeholder="ไม่กำหนด"
            className={inputClass}
            value={values.defectsLiabilityMonths}
            onChange={(e) => set('defectsLiabilityMonths', e.target.value)}
          />
        </Field>
        <Field label="เงินล่วงหน้าที่จ่ายแล้ว (บาท)" error={errors.advanceAmountPaid}>
          <input
            type="number"
            step="0.01"
            min="0"
            placeholder="ไม่กำหนด"
            className={inputClass}
            value={values.advanceAmountPaid}
            onChange={(e) => set('advanceAmountPaid', e.target.value)}
          />
        </Field>
        <Field label="วิธีหักคืนเงินล่วงหน้า">
          <select
            className={inputClass}
            value={values.advanceRecoveryMethod}
            onChange={(e) => set('advanceRecoveryMethod', e.target.value as AdvanceRecoveryMethod)}
          >
            <option value="ProRata">ProRata (หักตามสัดส่วนทุกงวด)</option>
            <option value="ThresholdBanded">ThresholdBanded (FIDIC 14.2)</option>
            <option value="Manual">Manual (กรอกเอง)</option>
          </select>
        </Field>

        {isThresholdBanded && (
          <>
            <Field label="เริ่มหักคืนเมื่อสะสมถึง (%)" error={errors.advanceRecoveryStartPct}>
              <input
                type="number"
                step="0.01"
                min="0"
                max="100"
                className={inputClass}
                value={values.advanceRecoveryStartPct}
                onChange={(e) => set('advanceRecoveryStartPct', e.target.value)}
              />
            </Field>
            <Field label="อัตราหักคืนส่วนเกิน (%)" error={errors.advanceRecoveryRatePct}>
              <input
                type="number"
                step="0.01"
                min="0"
                max="100"
                className={inputClass}
                value={values.advanceRecoveryRatePct}
                onChange={(e) => set('advanceRecoveryRatePct', e.target.value)}
              />
            </Field>
            <Field label="บังคับหักคืนครบเมื่อสะสมถึง (%)" error={errors.advanceRecoveryEndPct}>
              <input
                type="number"
                step="0.01"
                min="0"
                max="100"
                className={inputClass}
                value={values.advanceRecoveryEndPct}
                onChange={(e) => set('advanceRecoveryEndPct', e.target.value)}
              />
            </Field>
          </>
        )}
      </div>

      {serverError && (
        <p role="alert" className="mt-4 text-xs text-danger">
          {serverError}
        </p>
      )}

      <div className="mt-5 flex justify-end gap-2">
        <Button type="button" variant="secondary" size="sm" onClick={onCancel} disabled={saving}>
          ยกเลิก
        </Button>
        <Button type="submit" size="sm" loading={saving}>
          บันทึก
        </Button>
      </div>
    </form>
  )
}
