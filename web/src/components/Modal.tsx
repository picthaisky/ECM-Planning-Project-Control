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
        onClose()
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
  }, [isOpen, onClose])

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
          'w-full max-w-md rounded-card border border-border bg-surface p-6',
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
