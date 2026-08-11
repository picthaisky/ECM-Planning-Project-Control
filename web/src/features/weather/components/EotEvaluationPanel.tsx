import { Button, StatusPill } from '../../../components'
import { formatRatio } from '../../../utils/format'
import type { useEotEvaluation } from '../useEotEvaluation'
import {
  EOT_CONFIDENCE_LABELS,
  EOT_CRITICALITY_BASIS_EXPLANATIONS,
  EOT_CRITICALITY_BASIS_LABELS,
  EOT_EXCLUSION_REASON_LABELS,
  formatWeatherDate,
  formatWeatherDateTime,
} from '../weatherLabels'

export interface EotEvaluationPanelProps {
  eot: ReturnType<typeof useEotEvaluation>
  /** Role-gated by the caller — mirrors `ProjectEotEvaluationsController`'s own
   * `PM,Planning,QS,Admin` gate exactly (narrower than the weather-log write roles: `Site` is
   * excluded, since this produces an analytical schedule opinion, not raw site data). */
  canEvaluate: boolean
}

/**
 * The EOT evaluation card (S11-FE-01, US-11.1) — replaces the prototype's gold
 * "สิทธิ์ขยายสัญญา (EOT) — 12 วัน" tile (`docs/ECM Planning Prototype.dc.html` ~line 426).
 *
 * **This relabelling is ADR-0020 / domain-rules.md §2.2, not a cosmetic choice.** The evaluator
 * computes `EotEligibleDays` — a schedule fact (how many working days the computed completion date
 * moved) — never *entitlement* (whether the contract actually awards that time, which additionally
 * needs the weather to qualify as exceptional under FIDIC 8.4(c)/8.5(c) or เหตุสุดวิสัย under
 * ป.พ.พ. ม.8 — which ordinary Thai monsoon rain generally is **not** — plus timely notice, and
 * concurrency assessment). Presenting a schedule fact as an entitlement is exactly the conflation
 * ADR-0020 separates; this is the screen where that error would reach a real user and inform a real
 * claim, so the headline below is labelled "ผลกระทบต่อกำหนดแล้วเสร็จ (EOT ที่ประเมินได้)" — never
 * "สิทธิ์ขยายสัญญา" — and the §2.2 disclosure renders verbatim beneath it, every time, unconditionally.
 *
 * There is no `GET` for this resource (`api.ts#evaluateEot`'s own remarks) — the panel has no
 * "load the last evaluation" state; only a `POST` run in this session ever populates it, and a page
 * reload loses it (by design: re-evaluating is always a new, explicit, auditable act, never an
 * implicit background read).
 */
export function EotEvaluationPanel({ eot, canEvaluate }: EotEvaluationPanelProps) {
  const result = eot.result

  return (
    <div className="rounded-card border border-border bg-surface p-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h3 className="font-heading text-sm font-semibold text-navy">ผลกระทบต่อกำหนดแล้วเสร็จ (EOT ที่ประเมินได้)</h3>
          <p className="mt-0.5 max-w-2xl text-[10.5px] text-text-faint">
            คำนวณจากบันทึกสภาพอากาศที่มีผลในปัจจุบันและประวัติการคำนวณ CPM ของโครงการ — การประเมินแต่ละครั้งจะสร้างบันทึกใหม่แบบถาวร
            (ไม่กระทบวันที่/งบประมาณของโครงการเอง — ดูรายละเอียดด้านล่าง)
          </p>
        </div>

        {canEvaluate && (
          <Button size="sm" loading={eot.state === 'evaluating'} onClick={() => void eot.evaluate()}>
            {result ? 'ประเมิน EOT อีกครั้ง' : 'ประเมิน EOT'}
          </Button>
        )}
      </div>

      {!canEvaluate && !result && (
        <p className="mt-3 text-xs text-text-faint">บทบาทของคุณไม่สามารถประเมิน EOT ได้ (ต้องเป็น PM, Planning, QS หรือ Admin)</p>
      )}

      {eot.state === 'evaluating' && (
        <p role="status" className="mt-3 text-xs text-text-faint">
          กำลังประเมิน EOT...
        </p>
      )}

      {eot.state === 'error' && (
        <p role="alert" className="mt-3 text-xs text-danger">
          {eot.error}
        </p>
      )}

      {!result && eot.state !== 'evaluating' && (
        <p className="mt-3 text-xs text-text-faint">
          ยังไม่ได้ประเมิน EOT ในเซสชันนี้ — กด &quot;ประเมิน EOT&quot; ด้านบนเพื่อคำนวณ (ระบบไม่มีการเก็บผลการประเมินล่าสุดไว้ให้โหลดอัตโนมัติ
          ทุกครั้งที่ต้องการดูผลต้องกดประเมินใหม่ ซึ่งตรงกับหลักการที่ว่าการแก้ไขข้อมูลใดๆ ต้องนำไปสู่การประเมินใหม่เสมอ ไม่ใช่การเขียนทับของเดิม)
        </p>
      )}

      {result && (
        <div className="mt-4">
          <div className="flex flex-wrap items-end gap-3">
            <p className="font-heading text-3xl font-bold text-gold">
              <span data-testid="eot-eligible-days">{result.eotEligibleDays.toLocaleString('th-TH')}</span>{' '}
              <span className="text-base font-medium text-text-faint">วัน</span>
            </p>
            <StatusPill label={EOT_CONFIDENCE_LABELS[result.confidence]} tone={result.confidence === 'Substantiated' ? 'success' : 'warning'} />
            <StatusPill label={EOT_CRITICALITY_BASIS_LABELS[result.criticalityBasis]} tone="neutral" />
          </div>

          <div role="alert" className="mt-3 rounded-card border border-warning-text/30 bg-warning-text/10 p-3 text-[11px] text-warning-text">
            <p className="font-semibold">ตัวเลขนี้คือผลกระทบต่อกำหนดแล้วเสร็จตามตารางงาน ไม่ใช่สิทธิ์ตามสัญญา</p>
            <p className="mt-1">
              การได้รับสิทธิ์ขยายเวลาขึ้นกับเงื่อนไขสัญญา (ความ &quot;ผิดปกติ&quot; ของสภาพอากาศ, การแจ้งเหตุภายในกำหนด)
              และการพิจารณาความล่าช้าที่เกิดพร้อมกัน ซึ่งระบบยังไม่ได้ประเมินให้
            </p>
          </div>

          <ul className="mt-2 list-disc space-y-0.5 pl-4 text-[10.5px] text-text-faint">
            <li>
              {result.entitlementBasisAssessed
                ? 'ประเมินคุณสมบัติตามสัญญาแล้ว (สิทธิ์ EOT ตามเงื่อนไขสัญญา)'
                : 'ยังไม่ได้ประเมินคุณสมบัติตามสัญญา (สิทธิ์ EOT ตามเงื่อนไขสัญญา)'}
            </li>
            <li>
              {result.concurrencyAssessed
                ? 'พิจารณาความล่าช้าที่เกิดพร้อมกันแล้ว (Concurrent Delay)'
                : 'ยังไม่ได้พิจารณาความล่าช้าที่เกิดพร้อมกัน (Concurrent Delay)'}
            </li>
            <li>{EOT_CRITICALITY_BASIS_EXPLANATIONS[result.criticalityBasis]}</li>
          </ul>

          {result.confidence === 'Provisional' && (
            <p role="alert" className="mt-2 text-[10.5px] font-semibold text-danger">
              ผลลัพธ์นี้เป็นการประเมินเบื้องต้น (Provisional) — ไม่ควรใช้เป็นตัวเลขยืนยันสำหรับการเรียกร้องสิทธิ์
            </p>
          )}

          <div className="mt-4 grid grid-cols-2 gap-3 text-xs sm:grid-cols-4">
            <Metric label="ช่วงเวลาประเมิน" value={`${formatWeatherDate(result.windowStart)} – ${formatWeatherDate(result.windowEnd)}`} />
            <Metric label="ระยะเวลาตามแผนเดิม" value={`${result.asScheduledDurationDays.toLocaleString('th-TH')} วัน`} />
            <Metric label="ระยะเวลาหลังผลกระทบ" value={`${result.impactedDurationDays.toLocaleString('th-TH')} วัน`} />
            <Metric label="ประเมินเมื่อ" value={formatWeatherDateTime(result.evaluatedAt)} />
            <Metric label="วันหยุดงานที่นับได้ (รวม)" value={`${result.countableStoppageDayCount.toLocaleString('th-TH')} วัน`} />
            <Metric label="วันปฏิทินที่นับได้ (ไม่ซ้ำวัน)" value={`${result.distinctCountableDateCount.toLocaleString('th-TH')} วัน`} />
            <Metric label="บันทึกที่ยังไม่ระบุกิจกรรม" value={`${result.unattributedStoppageDayCount.toLocaleString('th-TH')} รายการ`} />
            {result.latestNoticeDate && (
              <Metric
                label="กำหนดแจ้งเหตุล่าสุด (อ้างอิงเท่านั้น)"
                value={`${formatWeatherDate(result.latestNoticeDate)}${result.noticeWindowExpired ? ' (พ้นกำหนดแล้ว)' : ''}`}
                tone={result.noticeWindowExpired ? 'danger' : undefined}
              />
            )}
          </div>

          <p className="mt-4 text-[11px] font-medium uppercase tracking-wide text-text-faint">
            กิจกรรมที่มีผลต่อการประเมิน (Drivers) — อธิบายว่าอ้างกิจกรรมใด
          </p>
          <p className="mt-1 text-[10.5px] text-text-faint">
            ตัวเลขต่อกิจกรรมด้านล่างไม่จำเป็นต้องรวมกันได้เท่ากับผลรวม {result.eotEligibleDays.toLocaleString('th-TH')} วันด้านบน —
            ผลรวมทั้งโครงข่าย (network) เป็นตัวเลขที่ถูกต้องเสมอ ตัวเลขต่อกิจกรรมตอบคำถามคนละข้อ (เช่น ถ้าตัดกิจกรรมนี้ออกอย่างเดียวจะเปลี่ยนผลเท่าไร)
          </p>

          {result.drivers.length === 0 ? (
            <p className="mt-2 text-xs text-text-faint">ไม่มีกิจกรรมที่ส่งผลต่อการประเมินนี้</p>
          ) : (
            <div className="mt-2 overflow-x-auto rounded-card border border-border">
              <table className="w-full min-w-[720px] text-xs">
                <thead>
                  <tr className="bg-surface-muted text-[10.5px] font-semibold uppercase tracking-wide text-text-faint">
                    <th className="px-3 py-2 text-left">กิจกรรม</th>
                    <th className="px-3 py-2 text-right">วันหยุดงานที่นับได้</th>
                    <th className="px-3 py-2 text-right">Float ณ วันที่เกิดเหตุ</th>
                    <th className="px-3 py-2 text-right">วิกฤตหรือไม่</th>
                    <th className="px-3 py-2 text-right">วิกฤตหลังผลกระทบ</th>
                    <th className="px-3 py-2 text-right">Indicative EOT</th>
                    <th className="px-3 py-2 text-right">Marginal EOT</th>
                    <th className="px-3 py-2 text-right">Float คงเหลือ</th>
                  </tr>
                </thead>
                <tbody>
                  {result.drivers.map((driver) => (
                    <tr key={`${driver.cpmRunId}-${driver.activityId}`} className="border-t border-border-subtle">
                      <td className="px-3 py-2">
                        <div className="font-medium text-text">{driver.activityCode}</div>
                        <div className="truncate text-[10.5px] text-text-faint">{driver.activityName}</div>
                      </td>
                      <td className="px-3 py-2 text-right text-text-muted">{driver.stoppageDays.toLocaleString('th-TH')}</td>
                      <td className="px-3 py-2 text-right text-text-muted">{driver.totalFloatAtRun.toLocaleString('th-TH')}</td>
                      <td className="px-3 py-2 text-right">
                        <StatusPill label={driver.wasCriticalAtRun ? 'วิกฤต' : 'ไม่วิกฤต'} tone={driver.wasCriticalAtRun ? 'danger' : 'neutral'} />
                      </td>
                      <td className="px-3 py-2 text-right">
                        <StatusPill
                          label={driver.isOnImpactedCriticalPath ? 'วิกฤต' : 'ไม่วิกฤต'}
                          tone={driver.isOnImpactedCriticalPath ? 'danger' : 'neutral'}
                        />
                      </td>
                      <td className="px-3 py-2 text-right text-text-muted">{driver.indicativeEotDays.toLocaleString('th-TH')}</td>
                      <td className="px-3 py-2 text-right text-text-muted">{driver.marginalEotDays.toLocaleString('th-TH')}</td>
                      <td className="px-3 py-2 text-right text-text-muted">
                        {driver.remainingFloatAfter.toLocaleString('th-TH')}
                        {driver.unclaimedFractionalHours !== null && Number(driver.unclaimedFractionalHours) > 0 && (
                          <div className="text-[10px] text-text-faint">
                            เศษ {formatRatio(driver.unclaimedFractionalHours, 2)} ชม. (ไม่นับต่อ)
                          </div>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}

          {result.sources.some((source) => source.exclusionReason !== null) && (
            <details className="mt-3 text-xs text-text-faint">
              <summary className="cursor-pointer select-none text-[11px] font-medium text-text-muted">
                บันทึกที่ไม่ถูกนับ และเหตุผล ({result.sources.filter((source) => source.exclusionReason !== null).length} รายการ)
              </summary>
              <ul className="mt-2 space-y-1 pl-2">
                {result.sources
                  .filter((source) => source.exclusionReason !== null)
                  .map((source) => (
                    <li key={source.dailyWeatherLogId} className="font-mono text-[10.5px]">
                      <span className="text-text-faint">{source.dailyWeatherLogId}</span>
                      {' — '}
                      <span>{EOT_EXCLUSION_REASON_LABELS[source.exclusionReason!]}</span>
                    </li>
                  ))}
              </ul>
            </details>
          )}
        </div>
      )}
    </div>
  )
}

function Metric({ label, value, tone }: { label: string; value: string; tone?: 'danger' }) {
  return (
    <div>
      <p className="text-[10.5px] text-text-faint">{label}</p>
      <p className={`mt-0.5 font-medium ${tone === 'danger' ? 'text-danger' : 'text-text'}`}>{value}</p>
    </div>
  )
}
