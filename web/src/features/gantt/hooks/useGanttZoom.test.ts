import { act, renderHook } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { useGanttZoom } from './useGanttZoom'
import { PX_PER_DAY } from '../timeScale'

/**
 * S6-FE-02 DoD: "เปลี่ยน zoom ไม่ทำให้ตำแหน่งอ้างอิงหาย" — changing the zoom level must not lose the
 * user's current scroll reference point (e.g. looking at March 2026 stays on March 2026, not the
 * project start). `clientWidth`/`scrollWidth` are read-only on a real `HTMLElement` in jsdom, so
 * this test overrides them per-instance (same technique `DataTable.test.tsx` already uses for
 * `offsetHeight`/`offsetWidth`) rather than mocking the whole element away.
 */
function makeScrollElement({
  scrollLeft,
  clientWidth,
  scrollWidth,
}: {
  scrollLeft: number
  clientWidth: number
  scrollWidth: number
}): HTMLDivElement {
  const el = document.createElement('div')
  el.scrollLeft = scrollLeft
  Object.defineProperty(el, 'clientWidth', { configurable: true, value: clientWidth })
  Object.defineProperty(el, 'scrollWidth', { configurable: true, value: scrollWidth })
  return el
}

describe('useGanttZoom', () => {
  it('defaults to the given initial zoom level', () => {
    const el = makeScrollElement({ scrollLeft: 0, clientWidth: 800, scrollWidth: 2000 })
    const { result } = renderHook(() => useGanttZoom({ current: el }, 'day'))
    expect(result.current.zoom).toBe('day')
  })

  it('preserves the focal date at the viewport center when switching from week to month zoom', () => {
    const viewportWidth = 800
    const oldPxPerDay = PX_PER_DAY.week
    const newPxPerDay = PX_PER_DAY.month
    const oldScrollLeft = 5000 // arbitrary "user is currently looking at March 2026" position, far enough into the content that the new-scale target is comfortably positive (not clamped)
    const scrollWidthAtNewZoom = 100_000 // generously larger than any scrollLeft this test computes

    const el = makeScrollElement({
      scrollLeft: oldScrollLeft,
      clientWidth: viewportWidth,
      scrollWidth: scrollWidthAtNewZoom,
    })

    const { result } = renderHook(() => useGanttZoom({ current: el }, 'week'))
    expect(result.current.zoom).toBe('week')

    const focalDayOffset = (oldScrollLeft + viewportWidth / 2) / oldPxPerDay
    const expectedNewScrollLeft = focalDayOffset * newPxPerDay - viewportWidth / 2

    act(() => result.current.changeZoom('month'))

    expect(result.current.zoom).toBe('month')
    // `useLayoutEffect` runs synchronously within `act`, so the corrected scrollLeft is already
    // applied to the real DOM element by the time this assertion runs.
    expect(el.scrollLeft).toBeCloseTo(expectedNewScrollLeft, 5)
  })

  it('clamps the corrected scrollLeft to the new zoom level\'s actual scrollable range, never past it', () => {
    const viewportWidth = 800
    // A tiny `scrollWidth` at the new zoom forces the naive target scrollLeft to be clamped.
    const el = makeScrollElement({ scrollLeft: 5000, clientWidth: viewportWidth, scrollWidth: 900 })

    const { result } = renderHook(() => useGanttZoom({ current: el }, 'day'))
    act(() => result.current.changeZoom('month'))

    const maxScrollLeft = 900 - viewportWidth // scrollWidth - clientWidth
    expect(el.scrollLeft).toBeLessThanOrEqual(maxScrollLeft)
    expect(el.scrollLeft).toBeGreaterThanOrEqual(0)
  })

  it('is a no-op (keeps the same scrollLeft) when re-selecting the already-active zoom level', () => {
    const el = makeScrollElement({ scrollLeft: 321, clientWidth: 800, scrollWidth: 5000 })
    const { result } = renderHook(() => useGanttZoom({ current: el }, 'week'))

    act(() => result.current.changeZoom('week'))

    expect(result.current.zoom).toBe('week')
    expect(el.scrollLeft).toBe(321)
  })
})
