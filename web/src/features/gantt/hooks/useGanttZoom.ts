import { useCallback, useLayoutEffect, useRef, useState } from 'react'
import type { RefObject } from 'react'
import { PX_PER_DAY, computeScrollLeftForZoomChange } from '../timeScale'
import type { ZoomLevel } from '../timeScale'

/**
 * Owns the Gantt's zoom level (S6-FE-02, US-6.1) and preserves the user's horizontal reference
 * point across a zoom change (DoD: "เปลี่ยน zoom ไม่ทำให้ตำแหน่งอ้างอิงหาย" — e.g. if the user was
 * looking at March 2026, switching from week to month view keeps March 2026 in view rather than
 * resetting to the project start).
 *
 * Mechanism: on `changeZoom`, capture the date currently at the horizontal center of the viewport
 * under the *old* scale (`computeScrollLeftForZoomChange`'s inverse framing), then — once the DOM
 * has re-rendered with the *new* scale's (wider or narrower) scrollable width — set `scrollLeft` so
 * that same date is back at the viewport center. The intermediate value lives in a ref, not state,
 * so it never itself triggers a render; only the `zoom` state change does.
 */
export function useGanttZoom(chartRef: RefObject<HTMLElement | null>, initialZoom: ZoomLevel = 'week') {
  const [zoom, setZoom] = useState<ZoomLevel>(initialZoom)
  const pendingScrollLeftRef = useRef<number | null>(null)

  const changeZoom = useCallback(
    (nextZoom: ZoomLevel) => {
      setZoom((currentZoom) => {
        if (nextZoom === currentZoom) return currentZoom

        const el = chartRef.current
        if (el) {
          pendingScrollLeftRef.current = computeScrollLeftForZoomChange({
            oldScrollLeft: el.scrollLeft,
            oldPxPerDay: PX_PER_DAY[currentZoom],
            newPxPerDay: PX_PER_DAY[nextZoom],
            viewportWidth: el.clientWidth,
          })
        }
        return nextZoom
      })
    },
    [chartRef],
  )

  // Runs after the DOM has re-rendered with the new zoom's content width, so the clamp below is
  // against the *new* scrollable range (`scrollWidth`), never the stale pre-zoom one.
  useLayoutEffect(() => {
    const el = chartRef.current
    if (!el || pendingScrollLeftRef.current === null) return

    const maxScrollLeft = Math.max(0, el.scrollWidth - el.clientWidth)
    el.scrollLeft = Math.min(Math.max(0, pendingScrollLeftRef.current), maxScrollLeft)
    pendingScrollLeftRef.current = null
    // `chartRef` is a stable ref object (identity never changes across renders) — listed for
    // exhaustive-deps correctness, not because it ever causes this effect to re-run on its own.
  }, [zoom, chartRef])

  return { zoom, changeZoom }
}
