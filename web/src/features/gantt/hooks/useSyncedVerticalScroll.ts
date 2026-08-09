import { useEffect } from 'react'
import type { RefObject } from 'react'

/**
 * Mirrors vertical scroll position between the Gantt's left label pane and right chart pane
 * (S6-FE-02 DoD: "เลื่อนแล้วหัวตารางกับแท่งตรงกันเสมอ" — after scrolling either pane, row labels and
 * their bars must never drift out of alignment). Deliberately native `addEventListener('scroll',
 * …)` plus direct `.scrollTop` assignment — never React state — so a continuous scroll gesture
 * never re-renders anything above the label pane's own virtualizer (`/cmplus-ui`'s "scroll/zoom
 * state outside React (refs) to avoid re-render storms").
 *
 * The `source` guard breaks the otherwise-circular mutual-scroll loop: setting `.scrollTop` on an
 * element dispatches a genuine native `scroll` event on *that* element too, which would otherwise
 * re-trigger the other side's handler forever. Browsers additionally never dispatch `scroll` when
 * the value doesn't actually change, which is the common steady-state case once both sides are
 * already aligned — the guard exists for the one round-trip in between, not as the only defense.
 */
export function useSyncedVerticalScroll(
  leftRef: RefObject<HTMLElement | null>,
  rightRef: RefObject<HTMLElement | null>,
): void {
  useEffect(() => {
    const left = leftRef.current
    const right = rightRef.current
    if (!left || !right) return undefined

    let source: 'left' | 'right' | null = null

    function onLeftScroll() {
      if (source === 'right') {
        source = null
        return
      }
      source = 'left'
      right!.scrollTop = left!.scrollTop
    }

    function onRightScroll() {
      if (source === 'left') {
        source = null
        return
      }
      source = 'right'
      left!.scrollTop = right!.scrollTop
    }

    left.addEventListener('scroll', onLeftScroll, { passive: true })
    right.addEventListener('scroll', onRightScroll, { passive: true })

    return () => {
      left.removeEventListener('scroll', onLeftScroll)
      right.removeEventListener('scroll', onRightScroll)
    }
  }, [leftRef, rightRef])
}
