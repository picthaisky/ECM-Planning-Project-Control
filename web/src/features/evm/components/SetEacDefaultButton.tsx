import { Button } from '../../../components'
import { useSetEacVariantDefault } from '../useSetEacVariantDefault'
import { VARIANT_SHORT_LABELS } from '../evmSelectors'
import type { EacVariant } from '../types'

export interface SetEacDefaultButtonProps {
  projectId: string
  /** The currently displayed (local-only) variant — what gets persisted if the user clicks. */
  selectedVariant: EacVariant
  /** The project's actual persisted `EacVariantDefault`, as last confirmed by the server (from the
   * initial `GET .../evm` load, or updated here after a successful save). */
  persistedDefault: EacVariant
  onSaved: (variant: EacVariant) => void
}

/**
 * S7-FE-03: "ตั้งเป็นค่าเริ่มต้นของโครงการ" — the *only* write path to `Project.EacVariantDefault`
 * (ADR-0007(f)). Persists whichever variant is currently selected in `EacVariantSelect`; merely
 * switching that dropdown never calls this — see `EvmPage.tsx`, where `onChange` only updates local
 * state.
 *
 * Disabled whenever the local selection already equals the persisted default (nothing to save) —
 * this is also a second, independent guarantee (beyond "the selector's `onChange` never calls this
 * hook itself") that browsing between variants can never silently change the project default: even
 * a stray click while already on the default variant is a no-op click on a disabled button.
 *
 * The caller wraps this in `RequireRole` (mirrors `ProjectMasterCard.tsx`/`WbsPage.tsx`'s established
 * pattern of gating at the call site, not inside the reusable component) — this component itself
 * has no role awareness.
 */
export function SetEacDefaultButton({
  projectId,
  selectedVariant,
  persistedDefault,
  onSaved,
}: SetEacDefaultButtonProps) {
  const { state, error, save } = useSetEacVariantDefault(projectId)
  const isAlreadyDefault = selectedVariant === persistedDefault

  async function handleClick() {
    const result = await save(selectedVariant)
    if (result) onSaved(result.eacVariantDefault)
  }

  return (
    <div className="flex flex-col items-start gap-1">
      <Button
        size="sm"
        variant="secondary"
        onClick={() => void handleClick()}
        loading={state === 'saving'}
        disabled={isAlreadyDefault || state === 'saving'}
      >
        {isAlreadyDefault
          ? `ค่าเริ่มต้นของโครงการ: ${VARIANT_SHORT_LABELS[persistedDefault]}`
          : 'ตั้งเป็นค่าเริ่มต้นของโครงการ'}
      </Button>
      {state === 'error' && error && (
        <p role="alert" className="text-[10.5px] text-danger">
          {error}
        </p>
      )}
    </div>
  )
}
