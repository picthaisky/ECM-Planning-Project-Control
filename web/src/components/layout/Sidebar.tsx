import { NavLink, useParams } from 'react-router-dom'
import { useAuthStore } from '../../store/authStore'
import { cx } from '../../utils/cx'
import { NAV_ENTRIES } from './navConfig'

/** Thai label per `CMPlus.Domain.Enums.UserRole` — the JWT claim only ever carries the role
 * itself (never a display name), so this is the closest honest "who is this" the sidebar footer
 * can show without fabricating a name field the backend does not issue. */
const ROLE_LABEL: Record<string, string> = {
  PM: 'Project Manager',
  Planning: 'Planning',
  Site: 'Site Engineer',
  QS: 'QS / Cost Engineer',
  Executive: 'Executive',
  Admin: 'Admin',
  ProjectDirector: 'Project Director',
}

/**
 * Navy sidebar (S4-FE-01, US-4.2): logo block, the 13-screen nav (gold active state, per
 * `navConfig.ts`), and a footer showing the logged-in user's role + a logout action. Matches the
 * prototype's `width:222px` sidebar, `34px` gold logo tile, and nav row proportions exactly
 * (ADR-0006) — see this component's test file for the visual-diff assertions (colors, widths,
 * active-state classes) checked against the prototype's literal inline-style values.
 */
export function Sidebar() {
  const { projectId } = useParams<{ projectId: string }>()
  const role = useAuthStore((state) => state.claims?.role)
  const logout = useAuthStore((state) => state.logout)

  return (
    <nav
      aria-label="เมนูหลัก"
      className="flex w-[222px] flex-none flex-col bg-navy text-white"
    >
      <div className="flex items-center gap-2.5 border-b border-white/10 px-4 pb-4 pt-[18px]">
        <div
          aria-hidden="true"
          className="grid h-[34px] w-[34px] shrink-0 place-items-center rounded-md bg-gold font-heading text-xs font-bold text-navy"
        >
          ECM
        </div>
        <div>
          <div className="font-heading text-[13px] font-bold tracking-[0.4px]">ECM-PLANNING</div>
          <div className="text-[10px] text-white/45">Project Control</div>
        </div>
      </div>

      <div className="flex flex-col gap-0.5 px-2.5 py-3.5 text-[13px]">
        {NAV_ENTRIES.map((entry) => (
          <NavLink
            key={entry.id}
            to={`/app/${projectId ?? ''}/${entry.path}`}
            className={({ isActive }) =>
              cx(
                'flex items-center gap-2.5 rounded-[7px] px-3 py-[9px] transition-colors',
                isActive
                  ? 'bg-gold font-semibold text-navy'
                  : 'font-normal text-white/75 hover:bg-white/10',
              )
            }
          >
            {({ isActive }) => (
              <>
                <span
                  aria-hidden="true"
                  className={cx(
                    'h-2 w-2 flex-none rounded-[2px]',
                    isActive ? 'bg-navy' : 'border-[1.5px] border-white/50',
                  )}
                />
                {entry.label}
              </>
            )}
          </NavLink>
        ))}
      </div>

      <div className="mt-auto flex items-center gap-2.5 border-t border-white/10 px-[18px] py-3.5">
        <div
          aria-hidden="true"
          className="grid h-[30px] w-[30px] flex-none place-items-center rounded-full bg-secondary text-xs font-semibold"
        >
          {role ? role.slice(0, 2).toUpperCase() : '—'}
        </div>
        <div className="min-w-0 flex-1 text-[11.5px] leading-tight">
          <div className="truncate font-semibold">
            {role ? (ROLE_LABEL[role] ?? role) : 'ไม่ทราบผู้ใช้งาน'}
          </div>
          <button
            type="button"
            onClick={logout}
            className="text-white/45 underline decoration-dotted hover:text-white/70"
          >
            ออกจากระบบ
          </button>
        </div>
      </div>
    </nav>
  )
}
