import type { UserRole } from '../store/authStore'

/**
 * Thai label per `CMPlus.Domain.Enums.UserRole` — the JWT claim only ever carries the role itself
 * (never a display name). Shared by `features/payment/` (`ApprovalChainBar`'s per-step role labels)
 * and `features/tenant-admin/` (the policy editor's role picker) — both need the same mapping
 * `components/layout/Sidebar.tsx` already has inline for its own footer; kept here as the one
 * reusable copy for new features rather than each one inventing its own (conventions.md "one fact
 * lives in one place").
 */
export const ROLE_LABEL: Record<UserRole, string> = {
  PM: 'Project Manager',
  Planning: 'Planning',
  Site: 'Site Engineer',
  QS: 'QS / Cost Engineer',
  Executive: 'Executive',
  Admin: 'Admin',
  ProjectDirector: 'Project Director',
}
