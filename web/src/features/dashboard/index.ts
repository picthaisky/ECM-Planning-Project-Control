export { DashboardPage } from './DashboardPage'

export { DashboardKpiRow } from './components/DashboardKpiRow'
export type { DashboardKpiRowProps } from './components/DashboardKpiRow'
export { DashboardSCurvePreview } from './components/DashboardSCurvePreview'
export type { DashboardSCurvePreviewProps } from './components/DashboardSCurvePreview'
export { DashboardCriticalPathPreview } from './components/DashboardCriticalPathPreview'
export type { DashboardCriticalPathPreviewProps } from './components/DashboardCriticalPathPreview'
export { DashboardWbsRollupCard } from './components/DashboardWbsRollupCard'
export type { DashboardWbsRollupCardProps } from './components/DashboardWbsRollupCard'
export { DashboardPhotoStripPlaceholder } from './components/DashboardPhotoStripPlaceholder'
export type { DashboardPhotoStripPlaceholderProps } from './components/DashboardPhotoStripPlaceholder'

export { useDashboardData } from './useDashboardData'
export type { DashboardLoadState } from './useDashboardData'

export {
  EAC_NULL_REASON_LABELS,
  EAC_VARIANT_FORMULA_LABELS,
  describeWarning,
  toneForSign,
  toneForRatioThreshold,
  selectTopCriticalActivities,
  buildWbsNodeLabelLookup,
  describeWeightWarning,
  describeMixedScopeNode,
} from './dashboardSelectors'
export type { MetricTone, WbsNodeLabel } from './dashboardSelectors'

export { getDashboard, DashboardApiError } from './api'
export type { GetDashboardOptions } from './api'

export type {
  EacVariant,
  EacNullReason,
  DashboardWeightWarningDto,
  DashboardProgressRollupDto,
  DashboardResponseDto,
} from './types'
