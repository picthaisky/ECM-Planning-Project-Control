export interface ScreenPlaceholderProps {
  title: string
}

/**
 * S4-FE-01 DoD: "build the shell/routing structure now so later sprints just add screen content."
 * Every nav entry not yet implemented (`components/layout/navConfig.ts`'s `IMPLEMENTED_SCREENS`)
 * routes to this instead of a 404/blank page, so the full 13-screen nav is real and clickable this
 * sprint even though most screens' content lands in Sprints 5-15.
 */
export function ScreenPlaceholder({ title }: ScreenPlaceholderProps) {
  return (
    <div className="flex h-full min-h-[320px] items-center justify-center rounded-card border border-dashed border-border bg-surface text-center">
      <div>
        <p className="font-heading text-lg font-semibold text-navy">{title}</p>
        <p className="mt-2 text-sm text-text-faint">หน้านี้จะพร้อมใช้งานในสปรินต์ถัดไป</p>
      </div>
    </div>
  )
}
