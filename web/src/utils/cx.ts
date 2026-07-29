/**
 * Join class name fragments, skipping falsy values.
 *
 * A small local stand-in for `clsx`/`classnames` — the component set added in
 * S1-FE-01 is the first place multiple conditional Tailwind classes get
 * composed, and pulling in a dependency for a ~5-line utility isn't worth it.
 */
export function cx(...parts: Array<string | false | null | undefined>): string {
  return parts.filter(Boolean).join(' ')
}
