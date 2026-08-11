import { cx } from '../../../utils/cx'
import { formatMoney } from '../../../utils/format'
import { UI_EAC_VARIANTS, VARIANT_ASSUMPTION_LABELS, VARIANT_SHORT_LABELS } from '../evmSelectors'
import type { EacVariant, EacVariantResponseDto } from '../types'

export interface EacVariantSelectProps {
  /** The full `EvmResponseDto.variants[]` array (all 5) — this component filters/orders it down to
   * `UI_EAC_VARIANTS` itself, so "only 3 in v1" (ADR-0007(d)) lives in exactly one place. */
  variants: EacVariantResponseDto[]
  selected: EacVariant
  /** Switching the selection is **local UI state only** — this callback never triggers a refetch;
   * the caller (`EvmPage`) already has every variant's numbers in hand (design.md §1.1's "no round
   * trip"). It also never persists anything — only `SetEacDefaultButton` writes
   * `Project.EacVariantDefault` (S7-FE-03 DoD). */
  onChange: (variant: EacVariant) => void
  disabled?: boolean
}

/**
 * S7-FE-02: the EAC variant selector, showing exactly 3 options (`CpiBased` default, `Atypical`,
 * `CpiSpiBased`) per ADR-0007(d). A native `<fieldset>`/radio group (not a custom
 * `role="radiogroup"` reimplementation) for free keyboard navigation and screen-reader semantics
 * (CLAUDE.md accessibility non-negotiable).
 *
 * Deliberately shows **no** comparative colouring between the 3 options (e.g. highlighting "the
 * cheapest"/"the most conservative" one) — that is precisely the shape of feature that could
 * reintroduce the ADR-0007 ordering trap (domain-decisions.md §1.5: the cheapest/most-expensive
 * variant swaps depending on whether CPI/SPI are above or below 1). Each option only ever shows its
 * own EAC preview value (or "—"), with a single selected/unselected visual state — never a
 * red/green comparison across options.
 */
export function EacVariantSelect({ variants, selected, onChange, disabled }: EacVariantSelectProps) {
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

        return (
          <label
            key={variant}
            className={cx(
              'flex min-w-[150px] flex-col rounded-card border px-3 py-1.5 text-left transition-colors',
              disabled ? 'cursor-not-allowed' : 'cursor-pointer',
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
                disabled={disabled}
                className="h-3 w-3 accent-navy"
              />
              <span className={cx('text-xs font-semibold', isSelected ? 'text-navy' : 'text-text-muted')}>
                {VARIANT_SHORT_LABELS[variant]}
              </span>
            </span>
            <span className="mt-0.5 text-[10.5px] leading-snug text-text-faint">
              {VARIANT_ASSUMPTION_LABELS[variant]}
            </span>
            <span className="mt-1 font-heading text-[12.5px] font-semibold text-navy">EAC {eacPreview}</span>
          </label>
        )
      })}
    </fieldset>
  )
}
