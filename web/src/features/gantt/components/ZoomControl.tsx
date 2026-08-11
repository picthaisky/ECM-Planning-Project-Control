import { cx } from '../../../utils/cx'
import { ZOOM_LABELS, ZOOM_LEVELS } from '../timeScale'
import type { ZoomLevel } from '../timeScale'

export interface ZoomControlProps {
  zoom: ZoomLevel
  onChange: (zoom: ZoomLevel) => void
}

/** วัน/สัปดาห์/เดือน segmented zoom control (S6-FE-02, US-6.1). A plain button group — the DoD is
 * about scale/reference-point behavior (`useGanttZoom`), not this control's own visual novelty. */
export function ZoomControl({ zoom, onChange }: ZoomControlProps) {
  return (
    <div role="group" aria-label="ระดับการซูม" className="ml-auto flex overflow-hidden rounded-card border border-border">
      {ZOOM_LEVELS.map((level) => (
        <button
          key={level}
          type="button"
          aria-pressed={zoom === level}
          onClick={() => onChange(level)}
          className={cx(
            'px-3 py-1.5 text-[11.5px] font-medium',
            zoom === level ? 'bg-navy text-white' : 'bg-surface text-text-muted hover:text-navy',
          )}
        >
          {ZOOM_LABELS[level]}
        </button>
      ))}
    </div>
  )
}
