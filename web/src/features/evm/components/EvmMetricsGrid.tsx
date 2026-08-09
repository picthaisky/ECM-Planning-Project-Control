import { StatTile } from '../../../components'
import type { StatTileTone } from '../../../components'
import { formatMoney, formatRatio } from '../../../utils/format'
import {
  VARIANT_FORMULA_LABELS,
  resolveTcpi,
  toneForRatioThreshold,
  toneForSign,
} from '../evmSelectors'
import type { MetricTone } from '../evmSelectors'
import type { EacVariantResponseDto, EvmResponseDto } from '../types'

export interface EvmMetricsGridProps {
  evm: EvmResponseDto
  /** The result entry matching whichever of the 3 UI variants is currently selected (local state,
   * `EvmPage`) — resolved once by the caller so this grid, `SCurveChart`'s forecast line and
   * `EacVariantSelect` never disagree about which variant's numbers are "current". */
  selectedVariantResult: EacVariantResponseDto | undefined
  state?: 'ready' | 'loading' | 'error'
  errorMessage?: string
}

const TONE_TO_STAT_TILE_TONE: Record<MetricTone, StatTileTone> = {
  neutral: 'neutral',
  success: 'success',
  danger: 'danger',
  gold: 'gold',
}

/** `null` -> `undefined` so `StatTile`'s own empty-state renders "—" — this grid never builds the
 * "—" string itself (S7-FE-01 DoD: null must render as "—", never 0; `StatTile` already owns that
 * exact rule, so it is applied in exactly one place, not re-implemented per tile). */
function money(value: string | null): string | undefined {
  return value === null ? undefined : `${formatMoney(value)} บาท`
}

function ratio(value: string | null): string | undefined {
  return value === null ? undefined : formatRatio(value)
}

interface MetricTileViewModel {
  key: string
  label: string
  value: string | undefined
  tone: MetricTone
  caption: string
}

/**
 * The 12-tile EVM metric grid (S7-FE-01 DoD: "ตาราง metric 12 ค่า", every value must match the API
 * response exactly). Every number is read straight off `EvmResponseDto` — this component never
 * recomputes a domain formula, it only formats, and (for TCPI only) picks which of two
 * already-computed backend fields to show (`resolveTcpi`).
 *
 * Tile #12 is **TCPI**, not the prototype mock's/`{@link '/cmplus-ui'}` skill's literal "% Complete"
 * slot: `EvmResponseDto` has no `%Complete` field at all — the backend computes and returns
 * `tcpiBac`/`tcpiEac` instead, matching `evm-formulas.md`'s own canonical 12-metric definition
 * (BAC, PV, EV, AC, SV, CV, SPI, CPI, EAC, ETC, VAC, TCPI) and this sprint's S7-BE-01/02 fixtures.
 * Flagged explicitly in this sprint's frontend report as a considered deviation from the
 * prototype's literal mock content, not an oversight.
 *
 * Colour policy (ADR-0007 "never assume a fixed variant ordering when colour-coding" —
 * domain-decisions.md §1.5, evm-formulas.md's Fixture D reversal): BAC/PV/ETC/EAC/TCPI stay neutral
 * navy regardless of which of the 3 EAC variants is selected — matching the prototype's own literal
 * tile colours (only SV/CV/SPI/CPI/VAC are dynamically coloured there too) — precisely so no tile
 * colour can ever encode a "this variant is the pessimistic one" assumption. VAC is the one
 * EAC-variant-derived tile that *is* colour-coded, and only from its own sign (`toneForSign`) — see
 * that function's own remarks for why this is safe under either ordering.
 */
export function EvmMetricsGrid({ evm, selectedVariantResult, state = 'ready', errorMessage }: EvmMetricsGridProps) {
  const tcpi = resolveTcpi(evm, selectedVariantResult)
  const variantLabel = selectedVariantResult ? VARIANT_FORMULA_LABELS[selectedVariantResult.variant] : null

  const tiles: MetricTileViewModel[] = [
    { key: 'bac', label: 'BAC', value: money(evm.bac), tone: 'neutral', caption: 'งบประมาณทั้งหมด (Budget at Completion)' },
    { key: 'pv', label: 'PV (BCWS)', value: money(evm.pv), tone: 'neutral', caption: 'มูลค่าตามแผน' },
    { key: 'ev', label: 'EV (BCWP)', value: money(evm.ev), tone: 'gold', caption: 'มูลค่างานที่ทำได้จริง' },
    { key: 'ac', label: 'AC (ACWP)', value: money(evm.ac), tone: 'danger', caption: 'ค่าใช้จ่ายจริง' },
    { key: 'sv', label: 'SV', value: money(evm.sv), tone: toneForSign(evm.sv), caption: 'EV − PV' },
    { key: 'cv', label: 'CV', value: money(evm.cv), tone: toneForSign(evm.cv), caption: 'EV − AC' },
    { key: 'spi', label: 'SPI', value: ratio(evm.spi), tone: toneForRatioThreshold(evm.spi), caption: 'EV / PV' },
    { key: 'cpi', label: 'CPI', value: ratio(evm.cpi), tone: toneForRatioThreshold(evm.cpi), caption: 'EV / AC' },
    {
      key: 'eac',
      label: 'EAC',
      value: money(selectedVariantResult?.eac ?? null),
      tone: 'neutral',
      caption: variantLabel ? `= ${variantLabel}` : 'ประมาณการเมื่อแล้วเสร็จ',
    },
    {
      key: 'etc',
      label: 'ETC',
      value: money(selectedVariantResult?.etc ?? null),
      tone: 'neutral',
      caption: 'EAC − AC',
    },
    {
      key: 'vac',
      label: 'VAC',
      value: money(selectedVariantResult?.vac ?? null),
      tone: toneForSign(selectedVariantResult?.vac ?? null),
      caption: 'BAC − EAC',
    },
    {
      key: 'tcpi',
      label: tcpi.basis === 'EAC' ? 'TCPI (EAC)' : 'TCPI (BAC)',
      value: ratio(tcpi.value),
      tone: 'neutral',
      caption: tcpi.basis === 'EAC' ? '(BAC − EV) / (EAC − AC)' : '(BAC − EV) / (BAC − AC)',
    },
  ]

  return (
    <div
      className="grid grid-cols-2 gap-3 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-6"
      data-testid="evm-metrics-grid"
    >
      {tiles.map((tile) => (
        <StatTile
          key={tile.key}
          label={tile.label}
          value={tile.value}
          caption={tile.caption}
          tone={TONE_TO_STAT_TILE_TONE[tile.tone]}
          state={state}
          errorMessage={errorMessage}
        />
      ))}
    </div>
  )
}
