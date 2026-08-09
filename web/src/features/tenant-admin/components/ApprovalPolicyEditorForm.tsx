import { useEffect, useId, useState } from 'react'
import { Button } from '../../../components'
import { cx } from '../../../utils/cx'
import { ROLE_LABEL } from '../../../utils/roleLabels'
import { MAX_CLIENT_QUORUM_COUNT, validatePolicyDraft } from '../bandValidation'
import type { RuleFieldIssues } from '../bandValidation'
import type { ApprovalPolicyLoadState } from '../useApprovalPolicy'
import type { UpdateApprovalPolicySaveState } from '../useUpdateApprovalPolicy'
import type {
  ApprovalDocumentType,
  ApprovalPolicy,
  ApprovalPolicyRule,
  UpdateApprovalPolicyPayload,
  UserRole,
} from '../types'

const ROLE_OPTIONS = Object.keys(ROLE_LABEL) as UserRole[]

function blankRule(nextStepNo: number): ApprovalPolicyRule {
  return { stepNo: nextStepNo, minAmount: '0.00', maxAmount: null, requiredRole: 'PM', quorumCount: 1 }
}

function fromPolicy(policy: ApprovalPolicy | null): ApprovalPolicyRule[] {
  return policy && policy.rules.length > 0 ? policy.rules.map((r) => ({ ...r })) : [blankRule(1)]
}

const inputClass =
  'w-full rounded-card border border-border px-2 py-1.5 text-xs text-text focus:border-navy focus:outline-none'

export interface ApprovalPolicyEditorFormProps {
  documentType: ApprovalDocumentType
  policy: ApprovalPolicy | null
  loadState: ApprovalPolicyLoadState
  loadError: string | null
  saveState: UpdateApprovalPolicySaveState
  saveError: string | null
  savedVersion: number | null
  onDismissSavedVersion: () => void
  onSave: (payload: UpdateApprovalPolicyPayload) => void
}

function fieldIssueFor(issues: RuleFieldIssues[], ruleIndex: number): RuleFieldIssues | undefined {
  return issues.find((i) => i.ruleIndex === ruleIndex)
}

/**
 * S9-FE-03: the amount-tiered approval-policy editor (steps × amount bands × roles × quorum),
 * Admin-only, reachable from the tenant-level entry point (`TenantAdminPage.tsx`), never the
 * per-project sidebar. Validates band overlaps/gaps **inline, before saving** (`bandValidation.ts`,
 * a faithful port of the real `ApprovalPolicy.ValidateBands`/`UpdateApprovalPolicyCommandHandler
 * .FindOverlappingStepNo`) and shows the new version number after a successful save
 * (`useUpdateApprovalPolicy.ts`).
 */
export function ApprovalPolicyEditorForm({
  documentType,
  policy,
  loadState,
  loadError,
  saveState,
  saveError,
  savedVersion,
  onDismissSavedVersion,
  onSave,
}: ApprovalPolicyEditorFormProps) {
  const [rules, setRules] = useState<ApprovalPolicyRule[]>(() => fromPolicy(policy))
  const [allowSelfApproval, setAllowSelfApproval] = useState(policy?.allowSelfApproval ?? false)
  const [escalationPct, setEscalationPct] = useState(policy?.cumulativeVoEscalationPct ?? '')
  const [escalationRole, setEscalationRole] = useState<UserRole | ''>(policy?.cumulativeVoEscalationRole ?? '')
  const formId = useId()

  // Re-seed the draft whenever a fresh load resolves (ready or not-configured) — never mid-edit
  // (loadState toggles back to 'loading' only on an explicit reload this form never triggers).
  useEffect(() => {
    if (loadState === 'ready' || loadState === 'not-configured') {
      setRules(fromPolicy(policy))
      setAllowSelfApproval(policy?.allowSelfApproval ?? false)
      setEscalationPct(policy?.cumulativeVoEscalationPct ?? '')
      setEscalationRole(policy?.cumulativeVoEscalationRole ?? '')
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps -- re-seed only on a fresh load resolving, not on every `policy` object identity change (there is none once loaded, this screen never mutates it in place)
  }, [loadState])

  const validation = validatePolicyDraft(rules)
  const isVariationOrder = documentType === 'VariationOrder'

  function updateRule(index: number, patch: Partial<ApprovalPolicyRule>) {
    setRules((current) => current.map((r, i) => (i === index ? { ...r, ...patch } : r)))
  }

  function addRule() {
    const nextStepNo = Math.max(0, ...rules.map((r) => r.stepNo)) + 1
    setRules((current) => [...current, blankRule(nextStepNo)])
  }

  function removeRule(index: number) {
    setRules((current) => current.filter((_, i) => i !== index))
  }

  function handleSave() {
    if (!validation.isValid || saveState === 'saving') return
    onSave({
      allowSelfApproval,
      cumulativeVoEscalationPct: escalationPct.trim() === '' ? null : escalationPct,
      cumulativeVoEscalationRole: escalationRole === '' ? null : escalationRole,
      rules,
    })
  }

  if (loadState === 'loading') {
    return <p className="text-xs text-text-faint">กำลังโหลดนโยบายอนุมัติ...</p>
  }
  if (loadState === 'error') {
    return (
      <p role="alert" className="text-xs text-danger">
        {loadError ?? 'โหลดนโยบายอนุมัติไม่สำเร็จ'}
      </p>
    )
  }

  return (
    <div className="flex flex-col gap-4">
      {loadState === 'not-configured' ? (
        <p className="rounded border border-dashed border-border px-3 py-2 text-[11.5px] text-text-faint">
          ยังไม่มีการตั้งค่านโยบายอนุมัติสำหรับประเภทเอกสารนี้ — การบันทึกครั้งแรกจะสร้าง Version 1
        </p>
      ) : (
        policy && (
          <p className="text-[11.5px] text-text-faint">
            เวอร์ชันปัจจุบันที่ใช้งานอยู่: <span className="font-semibold text-navy">v{policy.version}</span>
          </p>
        )
      )}

      {savedVersion !== null && (
        <p role="status" className="flex items-center gap-2 rounded border border-success/30 bg-success/10 px-3 py-2 text-[11.5px] text-success">
          บันทึกสำเร็จ — เวอร์ชันใหม่: v{savedVersion}
          <button type="button" onClick={onDismissSavedVersion} className="ml-auto text-success/70 hover:text-success">
            &times;
          </button>
        </p>
      )}

      <div className="flex items-center gap-2">
        <input
          id={`${formId}-self-approval`}
          type="checkbox"
          checked={allowSelfApproval}
          onChange={(e) => setAllowSelfApproval(e.target.checked)}
        />
        <label htmlFor={`${formId}-self-approval`} className="text-[12px] text-text">
          อนุญาตให้ผู้สร้าง/ผู้ยื่นเอกสารอนุมัติเอกสารของตนเองได้ (AllowSelfApproval — ปกติควรปิดไว้)
        </label>
      </div>

      {isVariationOrder && (
        <div className="grid grid-cols-2 gap-3 rounded-card border border-border bg-bg p-3">
          <div>
            <label className="block text-[11px] text-text-faint">Cumulative VO Escalation (%)</label>
            <input
              type="number"
              step="0.01"
              min="0"
              max="100"
              placeholder="ไม่ใช้ escalation"
              className={inputClass}
              value={escalationPct}
              onChange={(e) => setEscalationPct(e.target.value)}
            />
          </div>
          <div>
            <label className="block text-[11px] text-text-faint">บทบาทที่ต้องอนุมัติเพิ่มเมื่อสะสมเกิน</label>
            <select
              className={inputClass}
              value={escalationRole}
              onChange={(e) => setEscalationRole(e.target.value as UserRole | '')}
            >
              <option value="">— ไม่ระบุ —</option>
              {ROLE_OPTIONS.map((role) => (
                <option key={role} value={role}>
                  {ROLE_LABEL[role]}
                </option>
              ))}
            </select>
          </div>
        </div>
      )}

      <div>
        <div className="mb-1 flex items-center justify-between">
          <h4 className="text-[12px] font-semibold text-navy">ขั้นตอนอนุมัติ (Step × ช่วงจำนวนเงิน × บทบาท × Quorum)</h4>
          <Button type="button" size="sm" variant="secondary" onClick={addRule}>
            + เพิ่มขั้นตอน
          </Button>
        </div>

        <p className="mb-2 rounded border border-dashed border-border bg-bg px-3 py-2 text-[10.5px] text-text-faint">
          Quorum คือจำนวนผู้อนุมัติที่ต้องมีคนละคนกันในขั้นตอนเดียวกัน — ระบบจำกัดค่าสูงสุดไว้ที่{' '}
          {MAX_CLIENT_QUORUM_COUNT} คนในหน้านี้เพื่อป้องกันความผิดพลาด: หากตั้งค่าสูงกว่าจำนวนผู้ใช้ที่ถือบทบาทนั้นจริงในองค์กร
          เอกสารที่ส่งเข้าขั้นตอนนี้จะ<span className="font-semibold text-danger">ค้างอยู่ในสถานะรออนุมัติถาวร</span>
          โดยไม่มีวิธีถอนหรือยกเลิก — ค่านี้เป็นเพียงการป้องกันความผิดพลาดฝั่งหน้าจอเท่านั้น ระบบฝั่งเซิร์ฟเวอร์ยังไม่ได้บังคับเพดานนี้
        </p>

        <div className="overflow-x-auto rounded-card border border-border">
          <table className="w-full min-w-[720px] border-collapse text-xs">
            <thead>
              <tr className="bg-surface-muted text-[10.5px] uppercase tracking-wide text-text-faint">
                <th className="px-2 py-2 text-left">Step</th>
                <th className="px-2 py-2 text-left">จำนวนเงินขั้นต่ำ</th>
                <th className="px-2 py-2 text-left">จำนวนเงินสูงสุด (ว่าง = ไม่จำกัด)</th>
                <th className="px-2 py-2 text-left">บทบาทผู้อนุมัติ</th>
                <th className="px-2 py-2 text-left">Quorum</th>
                <th className="px-2 py-2" />
              </tr>
            </thead>
            <tbody>
              {rules.map((rule, index) => {
                const issue = fieldIssueFor(validation.fieldIssues, index)
                return (
                  <tr key={index} className="border-t border-border-subtle align-top">
                    <td className="px-2 py-2">
                      <input
                        type="number"
                        step="1"
                        min="1"
                        className={cx(inputClass, issue?.stepNo && 'border-danger')}
                        value={rule.stepNo}
                        onChange={(e) => updateRule(index, { stepNo: Number(e.target.value) })}
                      />
                      {issue?.stepNo && <p className="mt-1 text-[10px] text-danger">{issue.stepNo}</p>}
                    </td>
                    <td className="px-2 py-2">
                      <input
                        type="number"
                        step="0.01"
                        min="0"
                        className={cx(inputClass, issue?.minAmount && 'border-danger')}
                        value={rule.minAmount}
                        onChange={(e) => updateRule(index, { minAmount: e.target.value })}
                      />
                      {issue?.minAmount && <p className="mt-1 text-[10px] text-danger">{issue.minAmount}</p>}
                    </td>
                    <td className="px-2 py-2">
                      <input
                        type="number"
                        step="0.01"
                        min="0"
                        placeholder="ไม่จำกัด"
                        className={cx(inputClass, issue?.maxAmount && 'border-danger')}
                        value={rule.maxAmount ?? ''}
                        onChange={(e) => updateRule(index, { maxAmount: e.target.value === '' ? null : e.target.value })}
                      />
                      {issue?.maxAmount && <p className="mt-1 text-[10px] text-danger">{issue.maxAmount}</p>}
                    </td>
                    <td className="px-2 py-2">
                      <select
                        className={cx(inputClass, issue?.requiredRole && 'border-danger')}
                        value={rule.requiredRole}
                        onChange={(e) => updateRule(index, { requiredRole: e.target.value as UserRole })}
                      >
                        {ROLE_OPTIONS.map((role) => (
                          <option key={role} value={role}>
                            {ROLE_LABEL[role]}
                          </option>
                        ))}
                      </select>
                    </td>
                    <td className="px-2 py-2">
                      <input
                        type="number"
                        step="1"
                        min="1"
                        max={MAX_CLIENT_QUORUM_COUNT}
                        className={cx(inputClass, issue?.quorumCount && 'border-danger')}
                        value={rule.quorumCount}
                        onChange={(e) => updateRule(index, { quorumCount: Number(e.target.value) })}
                      />
                      {issue?.quorumCount && <p className="mt-1 text-[10px] text-danger">{issue.quorumCount}</p>}
                    </td>
                    <td className="px-2 py-2">
                      <button
                        type="button"
                        aria-label={`ลบขั้นตอนแถวที่ ${index + 1}`}
                        onClick={() => removeRule(index)}
                        disabled={rules.length === 1}
                        className="text-danger hover:text-danger/70 disabled:cursor-not-allowed disabled:opacity-40"
                      >
                        ลบ
                      </button>
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        </div>

        {validation.hasNoRules && (
          <p role="alert" className="mt-2 text-[10.5px] text-danger">
            ต้องมีอย่างน้อย 1 ขั้นตอน
          </p>
        )}
        {validation.bandProblems.length > 0 && (
          <ul className="mt-2 space-y-1">
            {validation.bandProblems.map((problem) => (
              <li key={`${problem.kind}-${problem.stepNo}`} role="alert" className="text-[10.5px] text-danger">
                {problem.kind === 'overlap'
                  ? `ขั้นตอนที่ ${problem.stepNo}: มีช่วงจำนวนเงินทับซ้อนกัน — ต้องมีเพียงกฎเดียวที่ครอบคลุมแต่ละช่วงจำนวนเงินในขั้นตอนนี้`
                  : `พบช่องว่างของขั้นตอน — ไม่มีกฎรองรับขั้นตอนที่ ${problem.stepNo} ในบางช่วงจำนวนเงิน กรุณาตรวจสอบให้ขั้นตอนต่อเนื่องกัน (1, 2, 3, ...)`}
              </li>
            ))}
          </ul>
        )}
      </div>

      {saveError && (
        <p role="alert" className="text-xs text-danger">
          {saveError}
        </p>
      )}

      <div className="flex justify-end">
        <Button onClick={handleSave} loading={saveState === 'saving'} disabled={!validation.isValid}>
          บันทึกนโยบาย
        </Button>
      </div>
    </div>
  )
}
