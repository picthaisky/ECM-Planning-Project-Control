import { useState } from 'react'
import type { FormEvent } from 'react'
import { Button, StatusPill } from '../../../components'
import { formatMoney } from '../../../utils/format'
import { ROLE_LABEL } from '../../../utils/roleLabels'
import type { RoutingSimulationState } from '../useRoutingSimulator'
import type { ApprovalDocumentType, ApprovalRoutingSimulation, SimulateApprovalRoutingPayload } from '../types'

const GUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

export interface RoutingSimulatorPanelProps {
  documentType: ApprovalDocumentType
  /** Convenience prefill only (e.g. the project currently selected elsewhere in the app) — this
   * screen is tenant-level, not project-scoped, so there is no implied project context and the
   * field stays freely editable. */
  defaultProjectId?: string | null
  state: RoutingSimulationState
  error: string | null
  result: ApprovalRoutingSimulation | null
  onSimulate: (payload: SimulateApprovalRoutingPayload) => void
}

/**
 * S15-FE-01 "ทดสอบเส้นทางอนุมัติ" (US-15.2) — the approval-routing simulator. Its entire value is
 * telling the truth: the backend (`SimulateApprovalRoutingQueryHandler`) resolves the chain through
 * the exact same `IApprovalPolicyReader.GetCandidatePoliciesAsync` -> `IApprovalRoutingService
 * .Resolve` path a real Submit uses, so this panel presents the result as exactly that — "if you
 * submit a ฿X document, this is who approves it, against policy version N" — never as a
 * re-derivation of its own. Two things this panel must never hide (this task's own DoD):
 *
 * 1. **ADR-0021.** `multipleActivePoliciesDetected` renders as a clear, un-missable warning naming
 *    the conflicting versions — never silently folded into a normal-looking chain.
 * 2. **Project is a real input, not an assumption.** Escalation (VariationOrder only) needs the
 *    project's baseline contract value; a Payment Certificate amount never triggers it, and this
 *    panel says so explicitly rather than leaving the Admin to infer it from an absent note.
 */
export function RoutingSimulatorPanel({
  documentType,
  defaultProjectId,
  state,
  error,
  result,
  onSimulate,
}: RoutingSimulatorPanelProps) {
  const [projectId, setProjectId] = useState(defaultProjectId ?? '')
  const [amount, setAmount] = useState('')
  const [fieldError, setFieldError] = useState<string | null>(null)

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const trimmedProjectId = projectId.trim()
    const trimmedAmount = amount.trim()

    if (!GUID_PATTERN.test(trimmedProjectId)) {
      setFieldError('รหัสโครงการต้องอยู่ในรูปแบบ GUID เช่น 3fa85f64-5717-4562-b3fc-2c963f66afa6')
      return
    }
    if (trimmedAmount === '' || !Number.isFinite(Number(trimmedAmount))) {
      setFieldError('กรุณาระบุจำนวนเงินสมมติที่ถูกต้อง')
      return
    }

    setFieldError(null)
    onSimulate({ projectId: trimmedProjectId, amount: trimmedAmount })
  }

  return (
    <div className="rounded-card border border-border bg-surface p-4" data-testid="approval-routing-simulator">
      <h3 className="font-heading text-[13.5px] font-semibold text-navy">ทดสอบเส้นทางอนุมัติ (Routing Simulator)</h3>
      <p className="mt-1 text-[11px] text-text-faint">
        กรอกจำนวนเงินสมมติและโครงการที่ต้องการทดลอง ระบบจะคำนวณเส้นทางอนุมัติด้วยวิธี resolve เดียวกันทุกประการกับตอนส่งเอกสารจริง
        — จะไม่มีการสร้างเอกสารใด ๆ ขึ้นจริงจากการทดลองนี้
      </p>

      {documentType === 'PaymentCertificate' && (
        <p className="mt-2 rounded border border-dashed border-border bg-bg px-3 py-2 text-[10.5px] text-text-faint">
          หมายเหตุ: เอกสารประเภท Payment Certificate ไม่มีเงื่อนไข escalation สะสมของ VO — ผลการทดลองด้านล่างจะไม่ประเมินเงื่อนไขนี้
          (escalation ใช้กับ Variation Order เท่านั้น)
        </p>
      )}

      <form onSubmit={handleSubmit} className="mt-3 grid gap-3 sm:grid-cols-[2fr_1fr_auto] sm:items-end">
        <div>
          <label className="block text-[11px] text-text-faint" htmlFor="simulator-project-id">
            รหัสโครงการ (Project ID)
          </label>
          <input
            id="simulator-project-id"
            type="text"
            placeholder="3fa85f64-5717-4562-b3fc-2c963f66afa6"
            value={projectId}
            onChange={(e) => setProjectId(e.target.value)}
            className="mt-1 w-full rounded-card border border-border px-2 py-1.5 font-mono text-xs text-text focus:border-navy focus:outline-none"
          />
        </div>
        <div>
          <label className="block text-[11px] text-text-faint" htmlFor="simulator-amount">
            จำนวนเงินสมมติ (บาท)
          </label>
          <input
            id="simulator-amount"
            type="number"
            step="0.01"
            placeholder="2400000.00"
            value={amount}
            onChange={(e) => setAmount(e.target.value)}
            className="mt-1 w-full rounded-card border border-border px-2 py-1.5 text-xs text-text focus:border-navy focus:outline-none"
          />
        </div>
        <Button type="submit" size="sm" loading={state === 'simulating'}>
          ทดลอง routing
        </Button>
      </form>

      {fieldError && (
        <p role="alert" className="mt-2 text-[10.5px] text-danger">
          {fieldError}
        </p>
      )}
      {error && (
        <p role="alert" className="mt-2 text-[10.5px] text-danger">
          {error}
        </p>
      )}

      {result && (
        <div className="mt-4 border-t border-border-subtle pt-3" data-testid="routing-simulation-result">
          {result.multipleActivePoliciesDetected && (
            <div
              role="alert"
              className="mb-3 rounded-card border border-danger/40 bg-danger/5 px-3 py-2.5 text-[11px] text-danger"
            >
              <p className="font-semibold">พบนโยบายที่ Active พร้อมกันมากกว่า 1 เวอร์ชันสำหรับขอบเขตนี้ (ADR-0021)</p>
              <p className="mt-1">
                สถานะข้อมูลนโยบายของ tenant นี้กำลังขัดแย้งกัน — เส้นทางด้านล่างคือสิ่งที่ระบบจะ resolve จริงในขณะนี้ (เวอร์ชัน v
                {result.approvalPolicyVersion}) แต่ผลลัพธ์อาจเปลี่ยนแปลงไม่แน่นอนระหว่างคำขอถัดไป เนื่องจากมีมากกว่าหนึ่งเวอร์ชันที่ Active
                พร้อมกัน กรุณาแก้ไขข้อมูลนโยบายให้เหลือเวอร์ชันที่ Active เพียงเวอร์ชันเดียวก่อนใช้งานจริง
              </p>
              <ul className="mt-1.5 flex flex-wrap gap-x-3 gap-y-0.5">
                {result.ambiguousActivePolicies.map((p) => (
                  <li key={p.approvalPolicyId}>
                    เวอร์ชันที่ขัดแย้งกัน: v{p.version} ({p.approvalPolicyId})
                  </li>
                ))}
              </ul>
            </div>
          )}

          <div className="flex flex-wrap items-center gap-2">
            <span className="text-[11.5px] text-text">
              จะถูก resolve ตามนโยบาย{' '}
              <span className="font-heading font-semibold text-navy">v{result.approvalPolicyVersion}</span>{' '}
              (เวอร์ชันเดียวกับที่จะใช้จริงหากส่งเอกสารในขณะนี้)
            </span>
            {result.usedFallbackChain && <StatusPill label="ใช้เส้นทางสำรอง (Fallback)" tone="warning" />}
          </div>

          <p className="mt-1 text-[11px] text-text-faint">
            จำนวนเงินที่ป้อน {formatMoney(result.inputAmount)} บาท
            {result.routingAmount !== result.inputAmount &&
              ` — ระบบใช้ ${formatMoney(result.routingAmount)} บาท (ค่าสัมบูรณ์) ในการกำหนดขั้นตอนอนุมัติ`}
          </p>

          {result.usedFallbackChain && (
            <p className="mt-1 text-[11px] text-warning-text">
              ยังไม่พบนโยบายที่ตั้งค่าไว้สำหรับ tenant นี้ ระบบจึงใช้เส้นทางสำรอง คือ Project Director อนุมัติเพียงคนเดียว —
              ควรตั้งค่านโยบายให้ครบถ้วนโดยเร็ว
            </p>
          )}

          <div className="mt-3 overflow-x-auto rounded-card border border-border">
            <table className="w-full min-w-[360px] border-collapse text-xs">
              <thead>
                <tr className="bg-surface-muted text-[10.5px] uppercase tracking-wide text-text-faint">
                  <th className="px-2 py-1.5 text-left">Step</th>
                  <th className="px-2 py-1.5 text-left">บทบาทผู้อนุมัติ</th>
                  <th className="px-2 py-1.5 text-left">Quorum</th>
                </tr>
              </thead>
              <tbody>
                {result.steps.map((step) => (
                  <tr key={step.stepNo} className="border-t border-border-subtle">
                    <td className="px-2 py-1.5">{step.stepNo}</td>
                    <td className="px-2 py-1.5">{ROLE_LABEL[step.requiredRole]}</td>
                    <td className="px-2 py-1.5">{step.quorumCount.toLocaleString('th-TH')}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <ul className="mt-3 flex flex-col gap-1 text-[11px] text-text-muted">
            <li>
              {result.allowSelfApproval
                ? 'อนุญาตให้ผู้สร้าง/ผู้ยื่นเอกสารอนุมัติเอกสารของตนเองได้'
                : 'ไม่อนุญาตให้ผู้สร้าง/ผู้ยื่นเอกสารอนุมัติเอกสารของตนเอง'}
            </li>
            <li>
              {documentType === 'PaymentCertificate'
                ? 'เงื่อนไข escalation สะสมของ VO ไม่เกี่ยวข้องกับเอกสารประเภทนี้'
                : result.escalationApplied
                  ? 'มีขั้นตอน escalation เพิ่มเติม — ยอด VO สะสมของโครงการนี้ (รวมจำนวนเงินที่ทดลองนี้) เทียบกับมูลค่าสัญญาตั้งต้นเกินเกณฑ์ที่ตั้งไว้'
                  : 'ไม่มีการ escalation เพิ่มเติม (ยอดสะสมยังไม่เกินเกณฑ์ หรือ tenant นี้ไม่ได้ตั้งค่า escalation ไว้)'}
            </li>
          </ul>
        </div>
      )}
    </div>
  )
}
