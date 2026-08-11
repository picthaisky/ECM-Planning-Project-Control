import { useParams } from 'react-router-dom'
import { EacAdvancedInputsCard } from './EacAdvancedInputsCard'
import { ImportWizard } from './ImportWizard'
import { ProjectMasterCard } from './ProjectMasterCard'

/**
 * S4-FE-02 "ข้อมูลโครงการ" screen (US-4.3/4.4) — the project master-data card (left column, this
 * sprint's new work) alongside Sprint 3's import wizard (right column, unchanged, only slotted
 * into this page per that component's own doc comment). Matches the prototype's
 * `grid-template-columns:1.4fr 1fr` two-column layout (`docs/ECM Planning Prototype.dc.html` line
 * ~454).
 *
 * S14-FE-02 adds `EacAdvancedInputsCard` beneath the master-data card, in the same (left) column —
 * the DoD's "ช่องกรอกบน Project Info" (`BottomUpEtc`/`CustomPf` inputs the EVM selector needs).
 */
export function ProjectInfoPage() {
  const { projectId } = useParams<{ projectId: string }>()
  if (!projectId) return null

  return (
    <div className="grid grid-cols-1 items-start gap-4 lg:grid-cols-[1.4fr_1fr]">
      <div className="flex flex-col gap-4">
        <ProjectMasterCard projectId={projectId} />
        <EacAdvancedInputsCard projectId={projectId} />
      </div>
      <ImportWizard projectId={projectId} />
    </div>
  )
}
