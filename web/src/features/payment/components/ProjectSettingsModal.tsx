import { Modal } from '../../../components'
import { ProjectEditForm } from '../../info'
import type { Project, UpdateProjectPayload } from '../../info'

export interface ProjectSettingsModalProps {
  isOpen: boolean
  onClose: () => void
  project: Project | null
  saving: boolean
  serverError: string | null
  onSubmit: (payload: UpdateProjectPayload) => void
}

/**
 * S9-FE-01 DoD: the "⚙ ตั้งค่า Retention/Advance" button opens **the same form as Project Info** —
 * a single source of truth for `Project.RetentionRate`/`AdvanceRate`/`RetentionCapPercentage`/
 * `AdvanceRecoveryMethod` etc., never a second, parallel settings UI that could drift from
 * `features/info/ProjectEditForm.tsx`. This wrapper adds no fields and no validation of its own —
 * it only supplies the modal chrome; `ProjectEditForm` is rendered completely unchanged, with its
 * own internal Cancel/Save buttons (so `Modal`'s own `footer` slot is deliberately left unused
 * here to avoid a second, duplicate action row).
 */
export function ProjectSettingsModal({ isOpen, onClose, project, saving, serverError, onSubmit }: ProjectSettingsModalProps) {
  return (
    <Modal isOpen={isOpen} onClose={onClose} title="ตั้งค่า Retention / Advance" className="max-w-2xl">
      {project ? (
        <ProjectEditForm project={project} saving={saving} serverError={serverError} onCancel={onClose} onSubmit={onSubmit} />
      ) : (
        <p className="text-xs text-text-faint">กำลังโหลดข้อมูลโครงการ...</p>
      )}
    </Modal>
  )
}
