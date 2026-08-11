import { useState } from 'react'
import { Button } from './Button'
import { OUTBOX_STATUS_LABELS, OUTBOX_STATUS_TONES } from './outboxStatusLabels'
import { StatusPill } from './StatusPill'
import { outboxKindLabel } from '../services/siteOutboxRegistry'
import type { OutboxSyncStatus } from '../services/useOutboxSyncStatus'

export interface SyncStatusBadgeProps {
  /** `services/useOutboxSyncStatus.ts` — passed in (not called internally) so this stays a pure,
   * easily-testable presentational component and so a single caller (`Sidebar.tsx`) can share *one*
   * hook instance between this badge's display and its own sign-out gate (N-03), rather than each
   * mounting an independent sync engine for the same data. */
  status: OutboxSyncStatus
}

/**
 * S13-FE-03 "UI สถานะการซิงค์ทั้งระบบ" (US-13.1): pending/failed counts, per-item Thai errors, and a
 * manual "ซิงค์ทั้งหมดตอนนี้" that works from any screen (`services/siteOutboxRegistry.ts` — every
 * `kind` registered, not only whichever feature page happens to be mounted).
 *
 * **Never claims automatic background sync where that is not true** (this sprint's explicit brief,
 * echoing `features/photo/PhotoPage.tsx`'s identical honesty): the capability line below reads
 * `syncCapability` directly rather than asserting a capability this device may not actually have
 * (Safari/iOS has no Background Sync API at all).
 */
export function SyncStatusBadge({ status }: SyncStatusBadgeProps) {
  const [open, setOpen] = useState(false)
  const { items, pendingCount, failedCount, conflictCount, syncCapability, isSyncing, syncNow } = status
  const attentionCount = failedCount + conflictCount
  const visibleItems = items.filter((item) => item.status !== 'synced')

  if (pendingCount === 0 && conflictCount === 0) {
    return (
      <button
        type="button"
        data-testid="sync-status-badge-toggle"
        onClick={() => setOpen((prev) => !prev)}
        className="text-left text-[10.5px] text-white/45 underline decoration-dotted hover:text-white/70"
      >
        ซิงค์ครบแล้ว
      </button>
    )
  }

  return (
    <div className="relative">
      <button
        type="button"
        data-testid="sync-status-badge-toggle"
        onClick={() => setOpen((prev) => !prev)}
        aria-expanded={open}
        // `text-gold`, not `text-warning-text` (`#9A7B1B`, tuned to pair with a *light* chip
        // background elsewhere in the app — see `theme.ts`) — this label sits directly on the navy
        // Sidebar background, and `warning-text`-on-navy falls under WCAG AA's 4.5:1 normal-text
        // floor at this size, while gold-on-navy comfortably clears it (already the design system's
        // own navy-background accent color, per ADR-0006).
        className="flex items-center gap-1.5 text-[10.5px] font-medium text-gold underline decoration-dotted hover:text-gold/70"
      >
        <span>รอซิงค์ {pendingCount.toLocaleString('th-TH')} รายการ</span>
        {attentionCount > 0 && (
          <span className="rounded-full bg-danger px-1.5 py-0.5 text-[9.5px] font-semibold text-white">
            {attentionCount.toLocaleString('th-TH')}
          </span>
        )}
      </button>

      {open && (
        <div
          role="dialog"
          aria-label="สถานะการซิงค์ทั้งระบบ"
          className="absolute bottom-full left-0 z-40 mb-2 w-80 rounded-card border border-border bg-surface p-3 text-text shadow-lg"
        >
          <div className="flex items-center justify-between gap-2">
            <div className="font-heading text-[12.5px] font-semibold text-navy">สถานะการซิงค์ทั้งระบบ</div>
            <Button size="sm" loading={isSyncing} onClick={() => void syncNow()}>
              ซิงค์ทั้งหมดตอนนี้
            </Button>
          </div>

          <p className="mt-1.5 text-[10.5px] text-text-faint">
            {syncCapability === 'background-sync'
              ? 'อุปกรณ์นี้รองรับการซิงค์อัตโนมัติเบื้องหลัง (Background Sync)'
              : 'อุปกรณ์นี้ไม่รองรับการซิงค์อัตโนมัติเบื้องหลัง (เช่น iOS/Safari) — ต้องเปิดแอปค้างไว้ขณะออนไลน์ หรือกดปุ่ม "ซิงค์ทั้งหมดตอนนี้"'}
          </p>

          <div className="mt-2 max-h-64 space-y-1.5 overflow-y-auto">
            {visibleItems.length === 0 ? (
              <p className="text-[10.5px] text-text-faint">ไม่มีรายการค้างซิงค์</p>
            ) : (
              visibleItems.map((item) => (
                <div
                  key={item.id}
                  data-testid="sync-status-item"
                  data-outbox-status={item.status}
                  className="rounded-card border border-border-subtle px-2 py-1.5"
                >
                  <div className="flex items-center justify-between gap-2">
                    <span className="text-[10.5px] font-medium text-text">{outboxKindLabel(item.kind)}</span>
                    <StatusPill label={OUTBOX_STATUS_LABELS[item.status]} tone={OUTBOX_STATUS_TONES[item.status]} />
                  </div>
                  {item.lastError && (item.status === 'failed' || item.status === 'conflict') && (
                    <p className="mt-1 text-[10px] text-danger">{item.lastError}</p>
                  )}
                </div>
              ))
            )}
          </div>
        </div>
      )}
    </div>
  )
}
