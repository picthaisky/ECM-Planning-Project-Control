import { useEffect, useId, useState } from 'react'
import type { FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { Button } from '../../components'
import { useAuthStore } from '../../store/authStore'
import type { UserRole } from '../../store/authStore'
import { useProjectMasterData } from './useProjectMasterData'
import { useSetEacAdvancedInputs } from './useSetEacAdvancedInputs'

export interface EacAdvancedInputsCardProps {
  projectId: string
}

/** Mirrors `ProjectsController.SetEacAdvancedInputs`'s `[Authorize(Roles = "PM,QS,Executive")]`
 * exactly (ADR-0007(f)) — deliberately narrower than `ProjectMasterCard.tsx`'s `EDIT_ROLES`
 * (which also allows Admin): the S14-BE-03 endpoints do not name Admin, so it is not carried over
 * here by assumption, same discipline `EvmPage.tsx#EAC_DEFAULT_ROLES` already applies to the
 * sibling `eac-default` endpoint. */
const EAC_ADVANCED_INPUTS_ROLES: readonly UserRole[] = ['PM', 'QS', 'Executive']

const EAC_VARIANT_LABEL: Record<string, string> = {
  CpiBased: 'CPI-Based',
  Atypical: 'Atypical',
  CpiSpiBased: 'CPI × SPI',
  BottomUpEtc: 'Bottom-Up ETC',
  CustomPf: 'Custom PF',
}

const inputClass =
  'mt-1 w-full rounded-card border border-border px-3 py-2 text-sm text-text focus:border-navy focus:outline-none'

interface FieldErrors {
  manualEtc?: string
  customPf?: string
}

/** Mirrors `Project.SetEacManualEtc`/`SetEacCustomPerformanceFactor`'s own guards
 * (evm-formulas.md's edge-case table) — client-visible rejection in Thai before the round trip,
 * the server re-checks independently regardless. */
function validate(manualEtcRaw: string, customPfRaw: string): FieldErrors {
  const errors: FieldErrors = {}
  if (manualEtcRaw.trim() !== '') {
    const value = Number(manualEtcRaw)
    if (Number.isNaN(value) || value < 0) errors.manualEtc = 'ต้องไม่ติดลบ'
  }
  if (customPfRaw.trim() !== '') {
    const value = Number(customPfRaw)
    if (Number.isNaN(value) || value <= 0) errors.customPf = 'ต้องมากกว่า 0'
  }
  return errors
}

/**
 * S14-FE-02 "ตั้งค่า EAC ขั้นสูง (Bottom-Up ETC / Custom PF)" — the Project Info half of the
 * 5-variant EAC selector (the other half, `EacVariantSelect`, lives on the EVM screen). DoD:
 * `BottomUpEtc`/`CustomPf` need a persisted input before they are selectable there; this is where
 * that input is set.
 *
 * Loads its own `ProjectDetail` via a second `useProjectMasterData(projectId)` instance (rather
 * than sharing `ProjectMasterCard`'s) — a deliberate, low-risk tradeoff: it costs one duplicate
 * `GET /api/v1/projects/{id}` per page load (today a 404 either way — see `api.ts#getProject`'s
 * remarks) in exchange for not touching `ProjectMasterCard`'s existing, already-tested props/shape
 * at all.
 *
 * **Full-representation PUT safety.** `setEacAdvancedInputs` always sends *both* fields — a field
 * left blank on submit is a genuine "clear", not "leave alone" (`SetEacAdvancedInputsCommand`'s own
 * remarks). This form only ever becomes interactive once `project` has actually loaded (`ready`
 * gate below), so the two number inputs are always seeded from the real current values first —
 * never submitted from an unset/blank-by-default state that could silently clear a value the user
 * never intended to touch.
 */
export function EacAdvancedInputsCard({ projectId }: EacAdvancedInputsCardProps) {
  const { project, loadState, loadError, reload, updateEacConfig } = useProjectMasterData(projectId)
  const { state: saveState, error: saveError, save, clearError } = useSetEacAdvancedInputs(projectId)
  const role = useAuthStore((state) => state.claims?.role)
  const canEdit = role !== undefined && role !== null && EAC_ADVANCED_INPUTS_ROLES.includes(role)

  const [manualEtc, setManualEtc] = useState('')
  const [customPf, setCustomPf] = useState('')
  const [fieldErrors, setFieldErrors] = useState<FieldErrors>({})
  const manualEtcId = useId()
  const customPfId = useId()

  useEffect(() => {
    if (project) {
      setManualEtc(project.eacManualEtc ?? '')
      setCustomPf(project.eacCustomPerformanceFactor ?? '')
    }
  }, [project])

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    clearError()
    const errors = validate(manualEtc, customPf)
    setFieldErrors(errors)
    if (Object.keys(errors).length > 0) return

    void save(
      {
        eacManualEtc: manualEtc.trim() === '' ? null : manualEtc.trim(),
        eacCustomPerformanceFactor: customPf.trim() === '' ? null : customPf.trim(),
      },
      updateEacConfig,
    )
  }

  return (
    <div className="rounded-card border border-border bg-surface p-[18px]">
      <div className="mb-1 font-heading text-sm font-semibold text-navy">ตั้งค่า EAC ขั้นสูง</div>
      <p className="mb-3.5 text-[11px] text-text-faint">
        ค่าสำหรับตัวแปร EAC แบบ Bottom-Up ETC และ Custom Performance Factor — เลือกใช้ตัวแปรเหล่านี้ได้ที่หน้า{' '}
        <Link to={`/app/${projectId}/evm`} className="text-navy underline">
          EVM S-Curve
        </Link>
      </p>

      {loadState === 'loading' && (
        <div role="status" className="py-6 text-center text-xs text-text-faint">
          กำลังโหลดข้อมูลโครงการ...
        </div>
      )}

      {loadState === 'error' && (
        <div role="alert" className="py-6 text-center">
          <p className="text-xs text-danger">{loadError}</p>
          <Button size="sm" variant="secondary" className="mt-3" onClick={reload}>
            ลองใหม่อีกครั้ง
          </Button>
        </div>
      )}

      {loadState === 'ready' && project && (
        <>
          <p className="mb-3 text-[11px] text-text-muted">
            ตัวแปร EAC เริ่มต้นของโครงการปัจจุบัน:{' '}
            <span className="font-semibold text-navy">
              {EAC_VARIANT_LABEL[project.eacVariantDefault] ?? project.eacVariantDefault}
            </span>
          </p>

          {project.eacManualEtcStaleSince && (
            <div className="mb-3 rounded-card border border-warning-text/30 bg-warning-text/5 px-3 py-2 text-[10.5px] text-warning-text">
              มี Variation Order ที่อนุมัติแล้วเปลี่ยน BAC หลังจากกรอกค่า Bottom-Up ETC นี้ครั้งล่าสุด — ค่าประมาณการนี้อาจไม่ทันปัจจุบันแล้ว
              กรุณากรอกค่าประมาณการใหม่ (การกรอกค่าใหม่ด้านล่างจะล้างคำเตือนนี้โดยอัตโนมัติ)
            </div>
          )}

          {canEdit ? (
            <form onSubmit={handleSubmit} noValidate aria-label="ตั้งค่า EAC ขั้นสูง">
              <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
                <div>
                  <label htmlFor={manualEtcId} className="block text-[11px] text-text-faint">
                    Bottom-Up ETC — ประมาณการงานที่เหลือ (บาท)
                  </label>
                  <input
                    id={manualEtcId}
                    type="number"
                    step="0.01"
                    min="0"
                    placeholder="ไม่ได้กำหนด"
                    className={inputClass}
                    value={manualEtc}
                    onChange={(e) => setManualEtc(e.target.value)}
                  />
                  {fieldErrors.manualEtc && (
                    <p role="alert" className="mt-1 text-[10.5px] text-danger">
                      {fieldErrors.manualEtc}
                    </p>
                  )}
                </div>
                <div>
                  <label htmlFor={customPfId} className="block text-[11px] text-text-faint">
                    Custom Performance Factor
                  </label>
                  <input
                    id={customPfId}
                    type="number"
                    step="0.0001"
                    min="0"
                    placeholder="ไม่ได้กำหนด"
                    className={inputClass}
                    value={customPf}
                    onChange={(e) => setCustomPf(e.target.value)}
                  />
                  {fieldErrors.customPf && (
                    <p role="alert" className="mt-1 text-[10.5px] text-danger">
                      {fieldErrors.customPf}
                    </p>
                  )}
                </div>
              </div>

              {saveError && (
                <p role="alert" className="mt-3 text-[11px] text-danger">
                  {saveError}
                </p>
              )}

              <div className="mt-4 flex justify-end">
                <Button type="submit" size="sm" loading={saveState === 'saving'}>
                  บันทึก
                </Button>
              </div>
            </form>
          ) : (
            <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 text-[12.5px]">
              <div>
                <div className="mb-1 text-[11px] text-text-faint">Bottom-Up ETC (บาท)</div>
                <div className="rounded-card border border-border bg-bg px-3 py-2.5 text-text">
                  {project.eacManualEtc ?? 'ไม่ได้กำหนด'}
                </div>
              </div>
              <div>
                <div className="mb-1 text-[11px] text-text-faint">Custom Performance Factor</div>
                <div className="rounded-card border border-border bg-bg px-3 py-2.5 text-text">
                  {project.eacCustomPerformanceFactor ?? 'ไม่ได้กำหนด'}
                </div>
              </div>
            </div>
          )}
        </>
      )}
    </div>
  )
}
