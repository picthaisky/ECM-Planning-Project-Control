import { Link } from 'react-router-dom'

export interface DashboardPhotoStripPlaceholderProps {
  projectId: string
}

/**
 * S8-FE-01's "Photo Progress ล่าสุด" slot (prototype row 3, right column). Photo Progress is Sprint
 * 12 — no `Photo`/gallery backend exists anywhere in this codebase yet, so there is no real recent
 * photo to show. Rendered as an honest, explicit "not available yet" placeholder (never decorative
 * fake thumbnails, which would look like real captured site photos) — the DoD names this widget's
 * *position*, not its content; see this sprint's frontend report for the full list of prototype
 * elements this sprint could not populate and why.
 */
export function DashboardPhotoStripPlaceholder({ projectId }: DashboardPhotoStripPlaceholderProps) {
  return (
    <div className="flex h-full flex-col rounded-card border border-border bg-surface p-4">
      <h3 className="font-heading text-sm font-semibold text-navy">Photo Progress ล่าสุด</h3>

      <div className="mt-3 flex flex-1 flex-col items-center justify-center gap-1.5 rounded-card border border-dashed border-border bg-bg px-4 py-8 text-center">
        <p className="text-xs font-medium text-text-muted">ยังไม่พร้อมใช้งาน</p>
        <p className="text-[10.5px] text-text-faint">
          โมดูล Photo Progress อยู่ระหว่างพัฒนา (Sprint 12) — ยังไม่มีรูปถ่ายให้แสดงในขณะนี้
        </p>
      </div>

      <Link to={`/app/${projectId}/photo`} className="mt-3 text-xs font-semibold text-navy hover:text-gold">
        ไปที่ Photo Progress →
      </Link>
    </div>
  )
}
