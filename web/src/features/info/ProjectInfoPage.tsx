import { useParams } from 'react-router-dom'
import { ImportWizard } from './ImportWizard'
import { ProjectMasterCard } from './ProjectMasterCard'

/**
 * S4-FE-02 "ข้อมูลโครงการ" screen (US-4.3/4.4) — the project master-data card (left column, this
 * sprint's new work) alongside Sprint 3's import wizard (right column, unchanged, only slotted
 * into this page per that component's own doc comment). Matches the prototype's
 * `grid-template-columns:1.4fr 1fr` two-column layout (`docs/ECM Planning Prototype.dc.html` line
 * ~454).
 */
export function ProjectInfoPage() {
  const { projectId } = useParams<{ projectId: string }>()
  if (!projectId) return null

  return (
    <div className="grid grid-cols-1 items-start gap-4 lg:grid-cols-[1.4fr_1fr]">
      <ProjectMasterCard projectId={projectId} />
      <ImportWizard projectId={projectId} />
    </div>
  )
}
