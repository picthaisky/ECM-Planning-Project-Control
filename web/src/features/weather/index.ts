export { WeatherPage } from './WeatherPage'

export { evaluateEot, listWeatherLogs, recordWeatherLog, recordWeatherLogCorrection, WeatherApiError } from './api'
export type { ListWeatherLogsOptions } from './api'

export { useWeatherLogs } from './useWeatherLogs'
export type { WeatherLogsLoadState } from './useWeatherLogs'

export { useWeatherLogActions } from './useWeatherLogActions'
export type { WeatherActionKind } from './useWeatherLogActions'

export { useEotEvaluation } from './useEotEvaluation'
export type { EotEvaluationState } from './useEotEvaluation'

export { buildWeatherChainInfo, effectiveWeatherLogs } from './weatherChain'
export type { WeatherChainState, WeatherLogChainInfo } from './weatherChain'

export { computeWeatherSummaryStats } from './weatherStats'
export type { WeatherSummaryStats } from './weatherStats'

export {
  buildWeatherLogRequestFields,
  emptyWeatherLogFormValues,
  todayDateInputValue,
  validateWeatherLogFormValues,
  weatherLogFormValuesFromEntry,
} from './weatherForm'
export type { WeatherLogFormValues } from './weatherForm'

export {
  EOT_CONFIDENCE_LABELS,
  EOT_CRITICALITY_BASIS_EXPLANATIONS,
  EOT_CRITICALITY_BASIS_LABELS,
  EOT_EXCLUSION_REASON_LABELS,
  WEATHER_CONDITION_LABELS,
  WEATHER_ENTRY_KIND_LABELS,
  WEATHER_IMPACT_LABELS,
  formatWeatherDate,
  formatWeatherDateTime,
  toDateInputValue,
  toRequestDate,
} from './weatherLabels'

export { ActivityIdChips } from './components/ActivityIdChips'
export { EotEvaluationPanel } from './components/EotEvaluationPanel'
export { WeatherCorrectionModal } from './components/WeatherCorrectionModal'
export type { WeatherCorrectionModalProps } from './components/WeatherCorrectionModal'
export { WeatherLogFormFields } from './components/WeatherLogFormFields'
export { WeatherLogTable } from './components/WeatherLogTable'
export { WeatherRecordModal } from './components/WeatherRecordModal'
export type { WeatherRecordModalProps } from './components/WeatherRecordModal'
export { WeatherSummaryTiles } from './components/WeatherSummaryTiles'

export type {
  EotConfidence,
  EotCriticalityBasis,
  EotEvaluationDriverDto,
  EotEvaluationDto,
  EotEvaluationRunDto,
  EotEvaluationSourceDto,
  EotExclusionReason,
  EvaluateEotPayload,
  RecordWeatherLogCorrectionPayload,
  RecordWeatherLogPayload,
  WeatherCondition,
  WeatherImpact,
  WeatherLogDto,
  WeatherLogEntryKind,
} from './types'
