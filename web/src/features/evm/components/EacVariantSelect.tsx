import { Link } from 'react-router-dom'
import { cx } from '../../../utils/cx'
import { formatMoney } from '../../../utils/format'
import {
  EAC_NULL_REASON_LABELS,
  MISSING_EAC_INPUT_REASON,
  UI_EAC_VARIANTS,
  VARIANT_ASSUMPTION_LABELS,
  VARIANT_SHORT_LABELS,
} from '../evmSelectors'
import type { EacVariant, EacVariantResponseDto } from '../types'

export interface EacVariantSelectProps {
  /** The full `EvmResponseDto.variants[]` array (all 5) — this component filters/orders it down to
   * `UI_EAC_VARIANTS` itself, so "which variants are choosable" (ADR-0007(d)/S14-FE-02) lives in
   * exactly one place. */
  variants: EacVariantResponseDto[]
  selected: EacVariant
  /** Switching the selection is **local UI state only** — this callback never triggers a refetch;
   * the caller (`EvmPage`) already has every variant's numbers in hand (design.md §1.1's "no round
   * trip"). It also never persists anything — only `SetEacDefaultButton` writes
   * `Project.EacVariantDefault` (S7-FE-03 DoD). */
  onChange: (variant: EacVariant) => void
  disabled?: boolean
  /** Used only to build the "ไปกรอกได้ที่หน้าข้อมูลโครงการ" link under a disabled `BottomUpEtc`/
   * `CustomPf` option (S14-FE-02 DoD: "a disabled option must explain where to set it"). Optional —
   * omitting it (e.g. in a context with no routed project id) simply omits the link, never breaks
   * the disabled state itself. */
  projectId?: string
}

/**
 * S7-FE-02/S14-FE-02: the EAC variant selector — all 5 engine variants (ADR-0007), in
 * `UI_EAC_VARIANTS`'s fixed order. A native `<fieldset>`/radio group (not a custom
 * `role="radiogroup"` reimplementation) for free keyboard navigation and screen-reader semantics
 * (CLAUDE.md accessibility non-negotiable).
 *
 * **S14-FE-02 DoD: `BottomUpEtc`/`CustomPf` are selectable only when their own project-level input
 * exists.** When `variants[]` reports the specific `ManualEtcNotSet`/`CustomPfNotSet` reason for
 * one of them (`MISSING_EAC_INPUT_REASON`, `evmSelectors.ts`), that one option's radio is disabled
 * *in addition to* the whole-fieldset `disabled` prop (role/saving gate) — never merely greyed out
 * with no explanation: the same reason text `EAC_NULL_REASON_LABELS` already surfaces elsewhere in
 * this screen renders under the option, plus a real navigable link to the Project Info screen where
 * that input is actually set (not a dead end — see this file's own DoD note and
 * `evmSelectors.ts#MISSING_EAC_INPUT_REASON`'s remarks on why this specific reason is the reliable
 * signal to key off).
 *
 * Deliberately shows **no** comparative colouring between options (e.g. highlighting "the
 * cheapest"/"the most conservative" one) — that is precisely the shape of feature that could
 * reintroduce the ADR-0007 ordering trap (domain-decisions.md §1.5: the cheapest/most-expensive
 * variant swaps depending on whether CPI/SPI are above or below 1). Each option only ever shows its
 * own EAC preview value (or "—"), with a single selected/unselected visual state — never a
 * red/green comparison across options.
 */
export function EacVariantSelect({ variants, selected, onChange, disabled, projectId }: EacVariantSelectProps) {
  return (
    <fieldset
      disabled={disabled}
      className={cx('flex flex-wrap items-stretch gap-2', disabled && 'opacity-60')}
    >
      <legend className="sr-only">เลือกตัวแปร EAC (Estimate at Completion)</legend>
      {UI_EAC_VARIANTS.map((variant) => {
        const result = variants.find((entry) => entry.variant === variant)
        const isSelected = variant === selected
        const eacPreview = result?.computable && result.eac !== null ? `${formatMoney(result.eac)} บาท` : '—'

        const missingInputReason = MISSING_EAC_INPUT_REASON[variant]
        const inputMissing = missingInputReason !== undefined && result?.reason === missingInputReason
        const optionDisabled = disabled || inputMissing

        return (
          <label
            key={variant}
            className={cx(
              'flex min-w-[150px] flex-col rounded-card border px-3 py-1.5 text-left transition-colors',
              optionDisabled ? 'cursor-not-allowed' : 'cursor-pointer',
              isSelected ? 'border-gold bg-gold/10' : 'border-border bg-surface hover:border-navy/40',
            )}
          >
            <span className="flex items-center gap-1.5">
              <input
                type="radio"
                name="eac-variant"
                value={variant}
                checked={isSelected}
                onChange={() => onChange(variant)}
                disabled={optionDisabled}
                className="h-3 w-3 accent-navy"
              />
              <span className={cx('text-xs font-semibold', isSelected ? 'text-navy' : 'text-text-muted')}>
                {VARIANT_SHORT_LABELS[variant]}
              </span>
            </span>
            <span className="mt-0.5 text-[10.5px] leading-snug text-text-faint">
              {VARIANT_ASSUMPTION_LABELS[variant]}
            </span>
            {inputMissing ? (
              <span className="mt-1 text-[10.5px] leading-snug text-warning-text">
                {EAC_NULL_REASON_LABELS[missingInputReason]}
                {projectId && (
                  <>
                    {' '}
                    <Link to={`/app/${projectId}/info`} className="underline">
                      ไปกรอกที่หน้าข้อมูลโครงการ →
                    </Link>
                  </>
                )}
              </span>
            ) : (
              <span className="mt-1 font-heading text-[12.5px] font-semibold text-navy">EAC {eacPreview}</span>
            )}
          </label>
        )
      })}
    </fieldset>
  )
}
