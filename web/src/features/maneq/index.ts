export { ManeqPage } from './ManeqPage'

export { getProductivityIndex, ManpowerApiError, recordManpowerLog, recordManpowerLogCorrection } from './api'
export type { GetProductivityIndexParams } from './api'

export { useManpowerLogActions } from './useManpowerLogActions'
export type { ManpowerActionKind } from './useManpowerLogActions'

export { useManpowerOverview } from './useManpowerOverview'
export type { HistogramPoint, LoadState, SectionState } from './useManpowerOverview'

export {
  addDaysToDateInputValue,
  cumulativeBucketRequest,
  dayBucketRequest,
  lastNDaysDateInputValues,
  monthToDateBucketRequest,
  startOfMonthDateInputValue,
  todayDateInputValue,
  toRequestDate,
} from './maneqDates'
export type { DateRequestBucket } from './maneqDates'

export {
  formatHours,
  formatManpowerDate,
  formatManpowerDateTime,
  formatShortDay,
  formatWorkerCount,
  LABOUR_TYPE_LABELS,
  MANPOWER_ENTRY_KIND_LABELS,
  PI_NULL_REASON_LABELS,
  PI_WARNING_LABELS,
  SHIFT_LABELS,
} from './maneqLabels'

export { isImplausiblePi, manningVarianceBand, parseManpowerDecimal, piBand } from './maneqStats'
export type { ManningVarianceBand, PiBand } from './maneqStats'

export { computeManpowerBarDomain, computeManpowerBarSlot, scaleManpowerBarValue } from './maneqBarScale'
export type { BarSlot, BarValueDomain } from './maneqBarScale'

export {
  buildRecordManpowerLogCorrectionPayload,
  buildRecordManpowerLogPayload,
  emptyManpowerLogFormValues,
  manpowerLogFormValuesFromEntry,
  validateManpowerLogFormValues,
} from './maneqForm'
export type { ManpowerLogFormValues } from './maneqForm'

export { ManpowerCorrectionModal } from './components/ManpowerCorrectionModal'
export { ManpowerHistogramChart } from './components/ManpowerHistogramChart'
export { ManpowerKpiTiles } from './components/ManpowerKpiTiles'
export { ManpowerLogFormFields } from './components/ManpowerLogFormFields'
export { ManpowerLogTable } from './components/ManpowerLogTable'
export { ManpowerRecordModal } from './components/ManpowerRecordModal'

export type {
  LabourType,
  ManpowerLogDto,
  ManpowerLogEntryKind,
  PiDataQualityWarning,
  PiNullReason,
  ProductivityIndexResponseDto,
  RecordManpowerLogCorrectionPayload,
  RecordManpowerLogPayload,
  Shift,
} from './types'
