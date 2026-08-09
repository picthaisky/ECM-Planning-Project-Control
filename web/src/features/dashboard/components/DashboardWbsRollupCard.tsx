import { Link } from 'react-router-dom'
import { formatPercent } from '../../../utils/format'
import type { WbsTreeNodeDto } from '../../wbs'
import { buildWbsNodeLabelLookup, describeMixedScopeNode, describeWeightWarning } from '../dashboardSelectors'
import type { DashboardProgressRollupDto } from '../types'

export interface DashboardWbsRollupCardProps {
  projectId: string
  rollup: DashboardProgressRollupDto
  rootNodes: WbsTreeNodeDto[]
  loadState: 'loading' | 'ready' | 'error'
  loadError: string | null
}

/**
 * S8-FE-01's "ความก้าวหน้าตาม WBS (ถ่วงน้ำหนัก)" card (prototype row 3, left column).
 *
 * Important, honest scope limit: the prototype's own mock shows a **per-branch** "แผน / จริง"
 * (plan vs actual) progress bar and a Δ column. No real data source for that exists anywhere this
 * screen can reach — `DashboardResponseDto.progressRollup` is a **project-level** aggregate only
 * (`WbsProgressRollupCalculator.Compute`'s own return shape has no per-node breakdown), the WBS tree
 * read (`GET .../wbs-tree`, reused here for the real branch/weight structure) carries no progress
 * figure at all, and the one endpoint that does carry per-activity progress
 * (`GET .../wbs-nodes/{id}/activities`) does not exist on the live API yet (`features/wbs/api.ts#
 * getNodeActivities`'s own doc comment). Rather than fabricate per-branch bars, this card shows the
 * real project-wide weighted total plus the real branch/weight structure, and says so explicitly —
 * see this sprint's frontend report.
 */
export function DashboardWbsRollupCard({
  projectId,
  rollup,
  rootNodes,
  loadState,
  loadError,
}: DashboardWbsRollupCardProps) {
  const lookup = buildWbsNodeLabelLookup(rootNodes)

  return (
    <div className="flex h-full flex-col overflow-hidden rounded-card border border-border bg-surface">
      <div className="flex items-baseline gap-3 px-4 pb-2 pt-3">
        <h3 className="font-heading text-sm font-semibold text-navy">ความก้าวหน้าตาม WBS (ถ่วงน้ำหนัก)</h3>
        <Link to={`/app/${projectId}/wbs`} className="ml-auto text-xs font-semibold text-navy hover:text-gold">
          เปิด WBS →
        </Link>
      </div>

      <div className="flex items-baseline gap-2 px-4 pb-3">
        <span className="font-heading text-2xl font-bold text-navy">
          {formatPercent(rollup.progressPercentage)}
        </span>
        <span className="text-[10.5px] text-text-faint">ความคืบหน้าถ่วงน้ำหนักรวมทั้งโครงการ</span>
      </div>

      {loadState === 'loading' && (
        <p role="status" className="px-4 pb-4 text-xs text-text-faint">
          กำลังโหลดโครงสร้าง WBS...
        </p>
      )}

      {loadState === 'error' && (
        <p role="alert" className="px-4 pb-4 text-xs text-danger">
          {loadError ?? 'โหลดโครงสร้าง WBS ไม่สำเร็จ'}
        </p>
      )}

      {loadState === 'ready' &&
        (rootNodes.length === 0 ? (
          <p className="px-4 pb-4 text-xs text-text-faint">ยังไม่มีโครงสร้าง WBS ในโครงการนี้</p>
        ) : (
          <table className="w-full text-[11.5px]">
            <thead>
              <tr className="bg-surface-muted text-left text-[10.5px] font-semibold uppercase tracking-wide text-text-faint">
                <th className="px-4 py-1.5 font-semibold">WBS</th>
                <th className="px-2 py-1.5 font-semibold">หมวดงาน</th>
                <th className="px-4 py-1.5 text-right font-semibold">Weight</th>
              </tr>
            </thead>
            <tbody>
              {rootNodes.map((node) => (
                <tr key={node.id} className="border-t border-border-subtle">
                  <td className="px-4 py-2 text-text-faint">{node.code}</td>
                  <td className="truncate px-2 py-2 text-text">{node.title}</td>
                  <td className="px-4 py-2 text-right text-text-muted">
                    {formatPercent(node.weightPercentage)}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        ))}

      <p className="mt-auto px-4 pb-3 pt-2 text-[10px] leading-snug text-text-faint">
        หมายเหตุ: ยังไม่มีข้อมูลความคืบหน้ารายหมวด (per-branch) บน endpoint นี้ — แสดงเฉพาะสัดส่วนน้ำหนัก
        (Weight) ต่อหมวดงานและยอดรวมถ่วงน้ำหนักทั้งโครงการด้านบนเท่านั้น
      </p>

      {rollup.weightWarnings.length > 0 && (
        <div
          role="alert"
          className="mx-4 mb-3 rounded-card border border-warning-text/30 bg-warning-text/5 px-3 py-2 text-[10.5px] text-warning-text"
        >
          {rollup.weightWarnings.map((warning, index) => (
            <p key={`${warning.wbsNodeId ?? 'root'}-${index}`}>{describeWeightWarning(warning, lookup)}</p>
          ))}
        </div>
      )}

      {rollup.mixedScopeWbsNodeIds.length > 0 && (
        <div className="mx-4 mb-3 rounded-card border border-border bg-bg px-3 py-2 text-[10.5px] text-text-muted">
          {rollup.mixedScopeWbsNodeIds.map((nodeId) => (
            <p key={nodeId}>{describeMixedScopeNode(nodeId, lookup)}</p>
          ))}
        </div>
      )}
    </div>
  )
}
