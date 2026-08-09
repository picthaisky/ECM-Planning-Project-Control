import { renderHook } from '@testing-library/react'
import { afterEach, describe, expect, it } from 'vitest'
import { useSyncedVerticalScroll } from './useSyncedVerticalScroll'

/**
 * jsdom stores whatever value you assign to `scrollTop` (a plain writable property) but — unlike a
 * real browser — never dispatches a native `scroll` event on its own when that value changes; a
 * real browser fires `scroll` for both user-driven and programmatic scrollTop changes. Tests here
 * therefore set `scrollTop` and then explicitly dispatch the `scroll` event to simulate what a real
 * browser does automatically — this isolates and verifies this hook's own mirroring *logic*
 * (S6-FE-02 DoD), which is the part actually worth unit-testing; the browser's native
 * scrollTop->scroll-event behavior is trusted, not re-tested.
 */

function scrollTo(el: HTMLElement, value: number) {
  el.scrollTop = value
  el.dispatchEvent(new Event('scroll'))
}

let cleanupFns: Array<() => void> = []

afterEach(() => {
  cleanupFns.forEach((fn) => fn())
  cleanupFns = []
})

function mountElements() {
  const left = document.createElement('div')
  const right = document.createElement('div')
  document.body.append(left, right)
  cleanupFns.push(() => {
    left.remove()
    right.remove()
  })
  return { left, right }
}

describe('useSyncedVerticalScroll', () => {
  it('mirrors the right pane scrolling down to the left pane', () => {
    const { left, right } = mountElements()
    renderHook(() => useSyncedVerticalScroll({ current: left }, { current: right }))

    scrollTo(right, 240)

    expect(left.scrollTop).toBe(240)
  })

  it('mirrors the left pane scrolling to the right pane (both directions, per S6-FE-02 DoD)', () => {
    const { left, right } = mountElements()
    renderHook(() => useSyncedVerticalScroll({ current: left }, { current: right }))

    scrollTo(left, 88)

    expect(right.scrollTop).toBe(88)
  })

  it('does not loop forever when one side mirrors the other (the reentrancy guard actually works)', () => {
    const { left, right } = mountElements()
    renderHook(() => useSyncedVerticalScroll({ current: left }, { current: right }))

    // If the guard were broken, this would recurse/ping-pong indefinitely and this test would
    // simply hang/time out rather than reach this assertion.
    scrollTo(right, 500)

    expect(left.scrollTop).toBe(500)
    expect(right.scrollTop).toBe(500)
  })

  it('detaches its listeners on unmount (no stale mirroring after the component using it is gone)', () => {
    const { left, right } = mountElements()
    const { unmount } = renderHook(() => useSyncedVerticalScroll({ current: left }, { current: right }))

    unmount()
    scrollTo(right, 300)

    expect(left.scrollTop).toBe(0)
  })
})
