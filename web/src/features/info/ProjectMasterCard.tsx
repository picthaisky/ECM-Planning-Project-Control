import { Button } from '../../components'
import { RequireRole } from '../../routes'
import { ProjectEditForm } from './ProjectEditForm'
import { calculateRetentionCapAmount, isAboveNormalRetentionCapThreshold } from './retentionCap'
import { useProjectMasterData } from './useProjectMasterData'
import { formatMoney, formatPercent } from '../../utils/format'

export interface ProjectMasterCardProps {
  projectId: string
}

/** Roles allowed to edit project master data — mirrors `ProjectsController`'s
 * `[Authorize(Roles = "PM,QS,Executive,Admin")]` exactly (S4-BE-02's own doc comment: Site/Planning
 * excluded, editing commercial terms is not a field-engineering concern). */
const EDIT_ROLES = ['PM', 'QS', 'Executive', 'Admin'] as const

const dateFormatter = new Intl.DateTimeFormat('th-TH', { dateStyle: 'long', timeZone: 'Asia/Bangkok' })

function formatDate(iso: string): string {
  const parsed = new Date(iso)
  return Number.isNaN(parsed.getTime()) ? iso : dateFormatter.format(parsed)
}

function ViewRow({ label, value, fullWidth }: { label: string; value: string; fullWidth?: boolean }) {
  return (
    <div className={fullWidth ? 'col-span-2' : undefined}>
      <div className="mb-1 text-[11px] text-text-faint">{label}</div>
      <div className="rounded-card border border-border bg-bg px-3 py-2.5 text-[12.5px] text-text">
        {value}
      </div>
    </div>
  )
}

/**
 * S4-FE-02 "ข้อมูลโครงการ (Project Master)" card — view/edit toggle, matching the prototype's
 * screen #2 left-column layout (`docs/ECM Planning Prototype.dc.html` line ~455). The edit button
 * is gated by `RequireRole` (S2-FE-02) so only PM/QS/Executive/Admin ever see it, mirroring the
 * backend's own role gate on `PUT /api/v1/projects/{id}` — read access (this card's view mode) is
 * open to any authenticated role.
 */
export function ProjectMasterCard({ projectId }: ProjectMasterCardProps) {
  const {
    project,
    loadState,
    loadError,
    reload,
    isEditing,
    startEdit,
    cancelEdit,
    saveState,
    saveError,
    save,
  } = useProjectMasterData(projectId)

  return (
    <div className="rounded-card border border-border bg-surface p-[18px]">
      <div className="mb-3.5 flex items-center">
        <div className="font-heading text-sm font-semibold text-navy">
          ข้อมูลโครงการ (Project Master)
        </div>
        {loadState === 'ready' && !isEditing && (
          <RequireRole allowedRoles={[...EDIT_ROLES]}>
            <Button size="sm" className="ml-auto" onClick={startEdit}>
              แก้ไขข้อมูล
            </Button>
          </RequireRole>
        )}
      </div>

      {loadState === 'loading' && (
        <div role="status" className="py-10 text-center text-xs text-text-faint">
          กำลังโหลดข้อมูลโครงการ...
        </div>
      )}

      {loadState === 'error' && (
        <div role="alert" className="py-6 text-center">
          <p className="text-xs text-danger">{loadError}</p>
          <Button size="sm" variant="secondary" className="mt-3" onClick={reload}>
            ลองใหม่อีกครั้ง
          </Button>
        </div>
      )}

      {loadState === 'ready' && project && isEditing && (
        <ProjectEditForm
          project={project}
          saving={saveState === 'saving'}
          serverError={saveError}
          onCancel={cancelEdit}
          onSubmit={(payload) => void save(payload)}
        />
      )}

      {loadState === 'ready' && project && !isEditing && (
        <div className="grid grid-cols-2 gap-3">
          <ViewRow label="ชื่อโครงการ" value={project.name} fullWidth />
          <ViewRow label="รหัสโครงการ" value={project.code} />
          <ViewRow label="เจ้าของโครงการ" value={project.owner} />
          <ViewRow label="วันเริ่มสัญญา" value={formatDate(project.contractStart)} />
          <ViewRow label="วันสิ้นสุดสัญญา" value={formatDate(project.contractFinish)} />
          <ViewRow label="มูลค่าสัญญา (BAC ตั้งต้น)" value={`${formatMoney(project.bac)} บาท`} />
          <ViewRow
            label="มูลค่าสัญญา (Contract Value)"
            value={`${formatMoney(project.contractValue)} บาท`}
          />
          <ViewRow
            label="Retention"
            value={
              project.retentionRate === null
                ? 'ไม่กำหนด'
                : `${formatPercent(project.retentionRate)}${
                    project.retentionCapPercentage === null
                      ? ' (ไม่มีเพดาน)'
                      : ` (เพดาน ${formatMoney(
                          calculateRetentionCapAmount(
                            Number(project.contractValue),
                            Number(project.retentionCapPercentage),
                          ) ?? 0,
                        )} บาท${
                          isAboveNormalRetentionCapThreshold(Number(project.retentionCapPercentage))
                            ? ' ⚠ สูงกว่าปกติ'
                            : ''
                        })`
                  }`
            }
          />
          <ViewRow
            label="เงินล่วงหน้า (Advance)"
            value={
              project.advanceRate === null
                ? 'ไม่กำหนด'
                : `${formatPercent(project.advanceRate)} · หักคืนแบบ ${project.advanceRecoveryMethod}`
            }
          />
        </div>
      )}
    </div>
  )
}
