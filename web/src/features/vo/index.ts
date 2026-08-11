export { VoPage } from './VoPage'

export {
  approveVariationOrder,
  cancelVariationOrder,
  createVariationOrder,
  getVariationOrder,
  getVoApprovalActions,
  listVariationOrders,
  rejectVariationOrder,
  returnVariationOrderForRevision,
  submitVariationOrder,
  updateVariationOrderContent,
  VoApiError,
  withdrawVariationOrder,
} from './api'

export { computeBacImpact } from './bacImpact'
export type { BacImpactInput, BacImpactResult, EscalationAssessment } from './bacImpact'

export {
  canAttemptApprove,
  canAttemptCancel,
  canAttemptEditContent,
  canAttemptReject,
  canAttemptReturnForRevision,
  canAttemptSubmit,
  canAttemptWithdraw,
  resolveChainStepTone,
} from './chainPermissions'
export type { ChainStepTone } from './chainPermissions'

export { buildEscalationNote } from './escalationNote'

export { useVariationOrders } from './useVariationOrders'
export type { VariationOrderReloadState, VariationOrdersLoadState } from './useVariationOrders'

export { useVoApprovalActions } from './useVoApprovalActions'
export type { ApprovalActionsState } from './useVoApprovalActions'

export { useVariationOrderActions } from './useVariationOrderActions'
export type { VoActionKind } from './useVariationOrderActions'

export { useVoContentSubmit } from './useVoContentSubmit'

export { computeStepVoteProgress } from './voteProgress'
export type { StepVoteProgress } from './voteProgress'

export { VO_STATUS_LABELS, VO_TYPE_LABELS } from './voStatusLabels'

export { BacImpactPanel } from './components/BacImpactPanel'
export type { BacImpactPanelProps } from './components/BacImpactPanel'
export { CancelVoModal } from './components/CancelVoModal'
export type { CancelVoModalProps } from './components/CancelVoModal'
export { VoContentModal } from './components/VoContentModal'
export type { VoContentModalProps } from './components/VoContentModal'
export { VoDetailPanel } from './components/VoDetailPanel'
export type { VoDetailPanelProps } from './components/VoDetailPanel'
export { VoSummaryTiles } from './components/VoSummaryTiles'
export type { VoSummaryTilesProps } from './components/VoSummaryTiles'
export { VoTable } from './components/VoTable'
export type { VoTableProps } from './components/VoTable'

export type {
  ApprovalActionDto,
  ApprovalActionType,
  ApprovalCommentPayload,
  CreateVariationOrderPayload,
  UpdateVariationOrderContentPayload,
  VariationOrderApprovalStepDto,
  VariationOrderDto,
  VariationOrderScopeItemDto,
  VariationOrderScopeItemInput,
  VariationOrderStatus,
  VariationOrderType,
} from './types'
