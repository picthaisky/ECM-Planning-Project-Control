import { useEffect, useId, useRef } from 'react'
import type { MouseEvent as ReactMouseEvent, ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { cx } from '../utils/cx'

export interface ModalProps {
  /** Controls mount/visibility. When `false` the Modal renders nothing (empty state). */
  isOpen: boolean
  onClose: () => void
  title?: ReactNode
  children: ReactNode
  /** Optional action row, e.g. Cancel/Confirm buttons for approve/reject dialogs. */
  footer?: ReactNode
  className?: string
}

const FOCUSABLE_SELECTOR = [
  'a[href]',
  'button:not([disabled])',
  'textarea:not([disabled])',
  'input:not([disabled])',
  'select:not([disabled])',
  '[tabindex]:not([tabindex="-1"])',
].join(',')

/**
 * Accessible modal dialog: traps focus, closes on Escape or backdrop click,
 * restores focus to the trigger element on close, and locks page scroll
 * while open. Used for approve/reject and other confirmation dialogs.
 */
export function Modal({ isOpen, onClose, title, children, footer, className }: ModalProps) {
  const panelRef = useRef<HTMLDivElement>(null)
  const previousFocusRef = useRef<HTMLElement | null>(null)
  const titleId = useId()

  /**
   * Holds the latest `onClose` without being a dependency of the focus-management effect below.
   *
   * Why this matters: many callers construct `onClose` as a fresh closure on every render (e.g. a
   * "reset local form state, then close" wrapper defined in the modal's own body — a completely
   * ordinary pattern, not a caller bug to work around by asking every consumer to `useCallback` it).
   * If `onClose` were a dependency of the effect below, then a modal whose *own* body holds editable
   * form state (almost all of them) would re-run that effect on every keystroke — and the effect
   * unconditionally moves focus to the first focusable element on setup. The visible symptom is
   * catastrophic: typing a second character into any field yanks focus away, so only ever the first
   * character of anything typed actually lands. Keeping `onClose` out of the dependency array (via
   * this ref, always current) means the effect only ever runs on a genuine `isOpen` transition, which
   * is the only time "move focus into the dialog" should happen at all.
   */
  const onCloseRef = useRef(onClose)
  useEffect(() => {
    onCloseRef.current = onClose
  })

  useEffect(() => {
    if (!isOpen) return undefined

    previousFocusRef.current =
      document.activeElement instanceof HTMLElement ? document.activeElement : null

    const panel = panelRef.current
    const focusables = panel
      ? Array.from(panel.querySelectorAll<HTMLElement>(FOCUSABLE_SELECTOR))
      : []
    ;(focusables[0] ?? panel)?.focus()

    const previousOverflow = document.body.style.overflow
    document.body.style.overflow = 'hidden'

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.preventDefault()
        onCloseRef.current()
        return
      }
      if (event.key !== 'Tab' || !panelRef.current) return

      const currentFocusables = Array.from(
        panelRef.current.querySelectorAll<HTMLElement>(FOCUSABLE_SELECTOR),
      )
      if (currentFocusables.length === 0) {
        event.preventDefault()
        return
      }
      const first = currentFocusables[0]
      const last = currentFocusables[currentFocusables.length - 1]

      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault()
        last.focus()
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault()
        first.focus()
      }
    }

    document.addEventListener('keydown', handleKeyDown)

    return () => {
      document.removeEventListener('keydown', handleKeyDown)
      document.body.style.overflow = previousOverflow
      previousFocusRef.current?.focus()
    }
    // onClose is intentionally not a dependency — it is read via onCloseRef above (see that ref's
    // own remarks); the project's react-hooks/exhaustive-deps configuration does not flag ref reads,
    // so no lint-disable is needed here, but the omission is still worth calling out explicitly.
  }, [isOpen])

  if (!isOpen) return null

  const handleBackdropMouseDown = (event: ReactMouseEvent<HTMLDivElement>) => {
    if (event.target === event.currentTarget) onClose()
  }

  return createPortal(
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-navy/50 p-4"
      onMouseDown={handleBackdropMouseDown}
    >
      <div
        ref={panelRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby={title ? titleId : undefined}
        tabIndex={-1}
        className={cx(
          // S13-FE-01: `max-h-[90vh] overflow-y-auto` (found via a real Playwright hang, not
          // theorized) — content taller than the viewport used to leave its own footer/confirm
          // button permanently unreachable, since `document.body.style.overflow = 'hidden'` (set
          // below while open) blocks the *page's* scroll and the panel itself had no scroll
          // mechanism of its own. A tall form (e.g. `WeatherCorrectionModal`'s full field set plus
          // its correction-reason fieldset) genuinely exceeds a phone-height viewport — the primary
          // target device for this app's site-facing screens (CLAUDE.md's "mobile-first") — so this
          // is a real usability fix, not merely a test workaround.
          'max-h-[90vh] w-full max-w-md overflow-y-auto rounded-card border border-border bg-surface p-6',
          className,
        )}
      >
        {title && (
          <h2 id={titleId} className="font-heading text-lg font-semibold text-navy">
            {title}
          </h2>
        )}
        <div className="mt-4 text-sm text-text">{children}</div>
        {footer && <div className="mt-6 flex justify-end gap-2">{footer}</div>}
      </div>
    </div>,
    document.body,
  )
}
