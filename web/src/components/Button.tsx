import { forwardRef } from 'react'
import type { ButtonHTMLAttributes, ReactNode } from 'react'
import { cx } from '../utils/cx'

export type ButtonVariant = 'primary' | 'secondary' | 'danger'
export type ButtonSize = 'sm' | 'md'

export interface ButtonProps extends Omit<ButtonHTMLAttributes<HTMLButtonElement>, 'children'> {
  /** Visual style. `primary` = solid navy, `secondary` = outlined navy, `danger` = solid red. */
  variant?: ButtonVariant
  size?: ButtonSize
  /** Shows a spinner and disables interaction (e.g. while an async action is in flight). */
  loading?: boolean
  children: ReactNode
}

const baseClasses =
  'inline-flex items-center justify-center gap-2 rounded-card font-medium transition-colors ' +
  'focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-gold ' +
  'disabled:cursor-not-allowed disabled:opacity-50'

const sizeClasses: Record<ButtonSize, string> = {
  sm: 'px-3 py-1.5 text-xs',
  md: 'px-4 py-2 text-sm',
}

const variantClasses: Record<ButtonVariant, string> = {
  primary: 'border border-navy bg-navy text-white hover:bg-navy/90',
  secondary: 'border border-navy bg-surface text-navy hover:bg-bg',
  danger: 'border border-danger bg-danger text-white hover:bg-danger/90',
}

/**
 * Base action button for the navy/gold system. Variants: primary (solid navy),
 * secondary (outlined navy), danger (solid red) — see `/cmplus-ui` top-bar
 * action patterns. Loading state disables the button and shows a spinner.
 */
export const Button = forwardRef<HTMLButtonElement, ButtonProps>(function Button(
  {
    variant = 'primary',
    size = 'md',
    loading = false,
    disabled = false,
    className,
    children,
    ...rest
  },
  ref,
) {
  const isDisabled = disabled || loading

  return (
    <button
      ref={ref}
      {...rest}
      type={rest.type ?? 'button'}
      disabled={isDisabled}
      aria-busy={loading || undefined}
      className={cx(baseClasses, sizeClasses[size], variantClasses[variant], className)}
    >
      {loading && (
        <span
          data-testid="button-spinner"
          aria-hidden="true"
          className="h-3.5 w-3.5 animate-spin rounded-full border-2 border-current border-t-transparent"
        />
      )}
      {children}
    </button>
  )
})
