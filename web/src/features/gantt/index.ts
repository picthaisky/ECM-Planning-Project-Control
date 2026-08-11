export { GanttPage } from './GanttPage'
export { GanttChart } from './components/GanttChart'
export type { GanttChartProps } from './components/GanttChart'
export { GanttLabelRow } from './components/GanttLabelRow'
export type { GanttLabelRowProps } from './components/GanttLabelRow'
export { GanttLegend } from './components/GanttLegend'
export { ZoomControl } from './components/ZoomControl'
export type { ZoomControlProps } from './components/ZoomControl'

export { useGanttData } from './useGanttData'
export type { GanttLoadState } from './useGanttData'
export { useGanttRowViewModels } from './hooks/useGanttRowViewModels'
export type { GanttRowViewModel } from './hooks/useGanttRowViewModels'
export { useSyncedVerticalScroll } from './hooks/useSyncedVerticalScroll'
export { useGanttZoom } from './hooks/useGanttZoom'

export { getGantt, GanttApiError } from './api'
export type { GanttActivityDto, GanttDto } from './types'

export {
  ZOOM_LEVELS,
  ZOOM_LABELS,
  PX_PER_DAY,
  ROW_HEIGHT,
  HEADER_HEIGHT,
  LABEL_PANE_WIDTH,
  computeTimelineBounds,
  computeScrollLeftForZoomChange,
  dateToX,
  xToDate,
  totalContentWidth,
  getHeaderTicks,
} from './timeScale'
export type { ZoomLevel, TimelineBounds, HeaderTick, ActivitySpan } from './timeScale'

export { drawBody, drawHeader } from './canvasDraw'
export type { DrawBodyParams, DrawHeaderParams, DrawingContext, GanttBarRow } from './canvasDraw'
