import { useId } from 'react'
import { LABOUR_TYPE_LABELS, SHIFT_LABELS } from '../maneqLabels'
import type { ManpowerLogFormValues } from '../maneqForm'
import type { ActivityOptionDto, LabourType, Shift, WbsNodeOptionDto, WeatherLogOptionDto, WorkCategoryDto } from '../types'

export interface ManpowerLogFormFieldsProps {
  values: ManpowerLogFormValues
  onChange: (patch: Partial<ManpowerLogFormValues>) => void
  disabled?: boolean
  /** The work-category catalogue for the dropdown. When empty (not loaded / endpoint unavailable),
   * the field degrades to a raw-GUID text input so logging is never blocked. */
  workCategories?: WorkCategoryDto[]
  /** The project's WBS nodes for the (optional) WBS-node dropdown; same empty-list fallback. */
  wbsNodes?: WbsNodeOptionDto[]
  /** Activities under the currently-selected WBS node for the (optional) dependent activity
   * dropdown; same empty-list fallback (empty when no node is selected). */
  activities?: ActivityOptionDto[]
  /** The project's weather logs for the (optional) related-weather-log dropdown; same fallback. */
  weatherLogs?: WeatherLogOptionDto[]
}

const inputClass =
  'mt-1 w-full rounded-card border border-border px-2.5 py-1.5 text-xs text-text focus:border-navy focus:outline-none disabled:bg-bg disabled:text-text-faint'
const guidInputClass = `${inputClass} font-mono`

const SHIFTS = Object.keys(SHIFT_LABELS) as Shift[]
const LABOUR_TYPES = Object.keys(LABOUR_TYPE_LABELS) as LabourType[]

/**
 * The `ManpowerEquipmentLog` field set shared by the "record" and "correct" forms (domain-rules.md
 * §4.1/§4.7 rule 6: a correction replaces every field). Every field name/shape mirrors
 * `RecordManpowerLogRequest` exactly (`backend/src/CMPlus.WebApi/Controllers/Manpower/ManpowerLogRequests.cs`).
 *
 * Every entity-reference field is a real dropdown when its source is available — `workCategoryId`
 * (`workCategories`, the catalogue), `wbsNodeId` (`wbsNodes`, the flattened WBS tree), `activityId`
 * (`activities`, the selected node's activities), and `relatedWeatherLogId` (`weatherLogs`) — each
 * degrading to a raw-GUID text input when empty/unavailable so logging is never blocked (the
 * graceful-degrade answer `features/weather/components/ActivityIdChips.tsx` established).
 */
export function ManpowerLogFormFields({ values, onChange, disabled, workCategories = [], wbsNodes = [], activities = [], weatherLogs = [] }: ManpowerLogFormFieldsProps) {
  const logDateId = useId()
  const shiftId = useId()
  const workCategoryId = useId()
  const wbsNodeId = useId()
  const activityId = useId()
  const labourTypeId = useId()
  const subcontractorRefId = useId()
  const workerCountId = useId()
  const manHoursId = useId()
  const overtimeHoursId = useId()
  const manHoursDerivedId = useId()
  const equipmentCountId = useId()
  const equipmentOperatingHoursId = useId()
  const equipmentStandbyHoursId = useId()
  const workDescriptionId = useId()
  const relatedWeatherLogIdId = useId()

  const showSubcontractorRef = values.labourType !== 'OwnDirect'

  return (
    <div className="grid grid-cols-2 gap-3">
      <div>
        <label htmlFor={logDateId} className="block text-[11px] text-text-faint">
          วันที่ (Log Date)
        </label>
        <input
          id={logDateId}
          type="date"
          disabled={disabled}
          value={values.logDate}
          onChange={(e) => onChange({ logDate: e.target.value })}
          className={inputClass}
        />
      </div>

      <div>
        <label htmlFor={shiftId} className="block text-[11px] text-text-faint">
          กะ (Shift)
        </label>
        <select
          id={shiftId}
          disabled={disabled}
          value={values.shift}
          onChange={(e) => onChange({ shift: e.target.value as Shift })}
          className={inputClass}
        >
          {SHIFTS.map((shift) => (
            <option key={shift} value={shift}>
              {SHIFT_LABELS[shift]}
            </option>
          ))}
        </select>
      </div>

      <div>
        <label htmlFor={workCategoryId} className="block text-[11px] text-text-faint">
          หมวดงาน (Work Category) — บังคับ
        </label>
        {workCategories.length > 0 ? (
          <select
            id={workCategoryId}
            disabled={disabled}
            value={values.workCategoryId}
            onChange={(e) => onChange({ workCategoryId: e.target.value })}
            className={inputClass}
          >
            <option value="">— เลือกหมวดงาน —</option>
            {workCategories.map((category) => (
              <option key={category.id} value={category.id}>
                {category.nameTh} ({category.code})
              </option>
            ))}
          </select>
        ) : (
          <input
            id={workCategoryId}
            type="text"
            disabled={disabled}
            placeholder="3fa85f64-5717-4562-b3fc-2c963f66afa6"
            value={values.workCategoryId}
            onChange={(e) => onChange({ workCategoryId: e.target.value })}
            className={guidInputClass}
          />
        )}
      </div>

      <div>
        <label htmlFor={wbsNodeId} className="block text-[11px] text-text-faint">
          WBS Node — ไม่บังคับ
        </label>
        {wbsNodes.length > 0 ? (
          <select
            id={wbsNodeId}
            disabled={disabled}
            value={values.wbsNodeId}
            onChange={(e) => onChange({ wbsNodeId: e.target.value })}
            className={inputClass}
          >
            <option value="">— ไม่ระบุ —</option>
            {wbsNodes.map((node) => (
              <option key={node.id} value={node.id}>
                {node.code} — {node.title}
              </option>
            ))}
          </select>
        ) : (
          <input
            id={wbsNodeId}
            type="text"
            disabled={disabled}
            placeholder="3fa85f64-5717-4562-b3fc-2c963f66afa6"
            value={values.wbsNodeId}
            onChange={(e) => onChange({ wbsNodeId: e.target.value })}
            className={guidInputClass}
          />
        )}
      </div>

      <div>
        <label htmlFor={activityId} className="block text-[11px] text-text-faint">
          กิจกรรม (Activity) — ไม่บังคับ
        </label>
        {activities.length > 0 ? (
          <select
            id={activityId}
            disabled={disabled}
            value={values.activityId}
            onChange={(e) => onChange({ activityId: e.target.value })}
            className={inputClass}
          >
            <option value="">— ไม่ระบุ —</option>
            {activities.map((activity) => (
              <option key={activity.id} value={activity.id}>
                {activity.activityCode} — {activity.name}
              </option>
            ))}
          </select>
        ) : (
          <input
            id={activityId}
            type="text"
            disabled={disabled}
            placeholder="3fa85f64-5717-4562-b3fc-2c963f66afa6"
            value={values.activityId}
            onChange={(e) => onChange({ activityId: e.target.value })}
            className={guidInputClass}
          />
        )}
      </div>

      <div>
        <label htmlFor={labourTypeId} className="block text-[11px] text-text-faint">
          ประเภทแรงงาน
        </label>
        <select
          id={labourTypeId}
          disabled={disabled}
          value={values.labourType}
          onChange={(e) => onChange({ labourType: e.target.value as LabourType })}
          className={inputClass}
        >
          {LABOUR_TYPES.map((labourType) => (
            <option key={labourType} value={labourType}>
              {LABOUR_TYPE_LABELS[labourType]}
            </option>
          ))}
        </select>
      </div>

      {showSubcontractorRef && (
        <div className="col-span-2">
          <label htmlFor={subcontractorRefId} className="block text-[11px] text-text-faint">
            ผู้รับเหมาช่วง / ผู้จ้างเหมา (Subcontractor Ref)
          </label>
          <input
            id={subcontractorRefId}
            type="text"
            disabled={disabled}
            maxLength={100}
            value={values.subcontractorRef}
            onChange={(e) => onChange({ subcontractorRef: e.target.value })}
            className={inputClass}
          />
        </div>
      )}

      <div>
        <label htmlFor={workerCountId} className="block text-[11px] text-text-faint">
          จำนวนคน (Worker Count)
        </label>
        <input
          id={workerCountId}
          type="number"
          step="1"
          min="0"
          disabled={disabled}
          value={values.workerCount}
          onChange={(e) => onChange({ workerCount: e.target.value })}
          className={`text-right ${inputClass}`}
        />
      </div>

      <div>
        <label htmlFor={manHoursId} className="block text-[11px] text-text-faint">
          ชั่วโมงแรงงานรวม (Man-Hours)
        </label>
        <input
          id={manHoursId}
          type="number"
          step="0.25"
          min="0"
          disabled={disabled}
          value={values.manHours}
          onChange={(e) => onChange({ manHours: e.target.value })}
          className={`text-right ${inputClass}`}
        />
      </div>

      <div>
        <label htmlFor={overtimeHoursId} className="block text-[11px] text-text-faint">
          ในนั้นเป็นชั่วโมงล่วงเวลา (Overtime)
        </label>
        <input
          id={overtimeHoursId}
          type="number"
          step="0.25"
          min="0"
          disabled={disabled}
          value={values.overtimeHours}
          onChange={(e) => onChange({ overtimeHours: e.target.value })}
          className={`text-right ${inputClass}`}
        />
      </div>

      <div className="flex items-end pb-1.5">
        <label htmlFor={manHoursDerivedId} className="flex items-center gap-2 text-[11px] text-text-faint">
          <input
            id={manHoursDerivedId}
            type="checkbox"
            disabled={disabled}
            checked={values.manHoursDerived}
            onChange={(e) => onChange({ manHoursDerived: e.target.checked })}
          />
          ชั่วโมงคำนวณจากจำนวนคน (≈) ไม่ได้วัดจริง
        </label>
      </div>

      <div>
        <label htmlFor={equipmentCountId} className="block text-[11px] text-text-faint">
          จำนวนเครื่องจักร
        </label>
        <input
          id={equipmentCountId}
          type="number"
          step="1"
          min="0"
          disabled={disabled}
          value={values.equipmentCount}
          onChange={(e) => onChange({ equipmentCount: e.target.value })}
          className={`text-right ${inputClass}`}
        />
      </div>

      <div>
        <label htmlFor={equipmentOperatingHoursId} className="block text-[11px] text-text-faint">
          ชั่วโมงทำงานเครื่องจักร
        </label>
        <input
          id={equipmentOperatingHoursId}
          type="number"
          step="0.25"
          min="0"
          disabled={disabled}
          value={values.equipmentOperatingHours}
          onChange={(e) => onChange({ equipmentOperatingHours: e.target.value })}
          className={`text-right ${inputClass}`}
        />
      </div>

      <div>
        <label htmlFor={equipmentStandbyHoursId} className="block text-[11px] text-text-faint">
          ชั่วโมง Standby เครื่องจักร
        </label>
        <input
          id={equipmentStandbyHoursId}
          type="number"
          step="0.25"
          min="0"
          disabled={disabled}
          value={values.equipmentStandbyHours}
          onChange={(e) => onChange({ equipmentStandbyHours: e.target.value })}
          className={`text-right ${inputClass}`}
        />
      </div>

      <div className="col-span-2">
        <label htmlFor={workDescriptionId} className="block text-[11px] text-text-faint">
          รายละเอียดงาน (เช่น &quot;โครงสร้าง ชั้น 9 + Curtain Wall&quot;) — ไม่บังคับ
        </label>
        <input
          id={workDescriptionId}
          type="text"
          disabled={disabled}
          maxLength={500}
          value={values.workDescription}
          onChange={(e) => onChange({ workDescription: e.target.value })}
          className={inputClass}
        />
      </div>

      <div className="col-span-2">
        <label htmlFor={relatedWeatherLogIdId} className="block text-[11px] text-text-faint">
          บันทึกสภาพอากาศที่เกี่ยวข้อง (Related Weather Log) — ไม่บังคับ
        </label>
        {weatherLogs.length > 0 ? (
          <select
            id={relatedWeatherLogIdId}
            disabled={disabled}
            value={values.relatedWeatherLogId}
            onChange={(e) => onChange({ relatedWeatherLogId: e.target.value })}
            className={inputClass}
          >
            <option value="">— ไม่ระบุ —</option>
            {weatherLogs.map((log) => (
              <option key={log.id} value={log.id}>
                {log.logDate.slice(0, 10)} — {log.condition}
              </option>
            ))}
          </select>
        ) : (
          <input
            id={relatedWeatherLogIdId}
            type="text"
            disabled={disabled}
            placeholder="3fa85f64-5717-4562-b3fc-2c963f66afa6"
            value={values.relatedWeatherLogId}
            onChange={(e) => onChange({ relatedWeatherLogId: e.target.value })}
            className={guidInputClass}
          />
        )}
      </div>
    </div>
  )
}
