export type { OutboxStorageAdapter } from './storage'
export { createIndexedDbOutboxStorage } from './storage'
export { createInMemoryOutboxStorage } from './storage.inMemory'

export { createOutboxStore, OutboxOwnerRequiredError, PENDING_STATUSES } from './outboxStore'
export type { CreateOutboxStoreOptions, OutboxStore } from './outboxStore'

export { DEFAULT_SYNCED_RETENTION_MS, purgeExpiredSyncedItems, quarantineOwnerBlobs } from './outboxMaintenance'
export type { PurgeExpiredSyncedItemsOptions } from './outboxMaintenance'

export { getCurrentOutboxOwner } from './authOwner'
export { registerOutboxLogoutQuarantine } from './authLifecycle'

export { createSyncEngine } from './syncEngine'
export type { FlushResult, OutboxUploader, SyncEngine, SyncEngineOptions } from './syncEngine'

export { detectSyncCapability, registerBackgroundSyncIfSupported, registerOutboxSyncTriggers } from './syncTriggers'
export type { OutboxSyncTriggersHandle, SyncCapability } from './syncTriggers'

export { generateOutboxId } from './idGenerator'

export type { EnqueueOutboxItemInput, OutboxItem, OutboxItemStatus, OutboxOwner } from './types'
