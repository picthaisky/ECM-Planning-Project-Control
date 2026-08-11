import { useId } from 'react'
import { WEATHER_CONDITION_LABELS, WEATHER_IMPACT_LABELS } from '../weatherLabels'
import { ActivityIdChips } from './ActivityIdChips'
import type { WeatherLogFormValues } from '../weatherForm'
import type { WeatherCondition, WeatherImpact } from '../types'

export interface WeatherLogFormFieldsProps {
  values: WeatherLogFormValues
  onChange: (patch: Partial<WeatherLogFormValues>) => void
  /** Greys out every data field (used by `WeatherCorrectionModal` when `EntryKind = Retraction`,
   * since a retraction voids the entry rather than replacing its content — only the reason matters,
   * though the underlying values still round-trip to the request per §8.2's DTO shape). */
  disabled?: boolean
}

const inputClass =
  'mt-1 w-full rounded-card border border-border px-2.5 py-1.5 text-xs text-text focus:border-navy focus:outline-none disabled:bg-bg disabled:text-text-faint'

const WEATHER_CONDITIONS = Object.keys(WEATHER_CONDITION_LABELS) as WeatherCondition[]
const WEATHER_IMPACTS = Object.keys(WEATHER_IMPACT_LABELS) as WeatherImpact[]

/** The `DailyWeatherLog` field set shared by the "record" and "correct" forms (domain-rules.md
 * weather-eot §8.1/§8.2 rule 6: a correction replaces every field, so both forms edit the same
 * set). Pure presentation — all state lives in the caller's `WeatherLogFormValues`. */
export function WeatherLogFormFields({ values, onChange, disabled }: WeatherLogFormFieldsProps) {
  const logDateId = useId()
  const conditionId = useId()
  const conditionNoteId = useId()
  const rainfallId = useId()
  const impactId = useId()
  const impactNoteId = useId()
  const hoursLostId = useId()

  const hoursLostRequired = values.impact !== 'NoImpact'

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
        <label htmlFor={conditionId} className="block text-[11px] text-text-faint">
          สภาพอากาศ
        </label>
        <select
          id={conditionId}
          disabled={disabled}
          value={values.condition}
          onChange={(e) => onChange({ condition: e.target.value as WeatherCondition })}
          className={inputClass}
        >
          {WEATHER_CONDITIONS.map((condition) => (
            <option key={condition} value={condition}>
              {WEATHER_CONDITION_LABELS[condition]}
            </option>
          ))}
        </select>
      </div>

      <div className="col-span-2">
        <label htmlFor={conditionNoteId} className="block text-[11px] text-text-faint">
          รายละเอียดสภาพอากาศเพิ่มเติม (ไม่บังคับ)
        </label>
        <input
          id={conditionNoteId}
          type="text"
          disabled={disabled}
          maxLength={200}
          value={values.conditionNote}
          onChange={(e) => onChange({ conditionNote: e.target.value })}
          className={inputClass}
        />
      </div>

      <div>
        <label htmlFor={rainfallId} className="block text-[11px] text-text-faint">
          ปริมาณฝน (มม. ใน 24 ชม.) — ไม่บังคับ
        </label>
        <input
          id={rainfallId}
          type="number"
          step="0.01"
          min="0"
          disabled={disabled}
          value={values.rainfallMm}
          onChange={(e) => onChange({ rainfallMm: e.target.value })}
          className={`text-right ${inputClass}`}
        />
      </div>

      <div>
        <label htmlFor={impactId} className="block text-[11px] text-text-faint">
          ผลกระทบต่องาน
        </label>
        <select
          id={impactId}
          disabled={disabled}
          value={values.impact}
          onChange={(e) => onChange({ impact: e.target.value as WeatherImpact })}
          className={inputClass}
        >
          {WEATHER_IMPACTS.map((impact) => (
            <option key={impact} value={impact}>
              {WEATHER_IMPACT_LABELS[impact]}
            </option>
          ))}
        </select>
      </div>

      <div className="col-span-2">
        <label htmlFor={impactNoteId} className="block text-[11px] text-text-faint">
          รายละเอียดผลกระทบ (เช่น &quot;หยุดเทคอนกรีตโซน B ครึ่งวัน&quot;) — ไม่บังคับ
        </label>
        <input
          id={impactNoteId}
          type="text"
          disabled={disabled}
          maxLength={500}
          value={values.impactNote}
          onChange={(e) => onChange({ impactNote: e.target.value })}
          className={inputClass}
        />
      </div>

      <div>
        <label htmlFor={hoursLostId} className="block text-[11px] text-text-faint">
          ชั่วโมงที่หยุดงาน{hoursLostRequired ? ' (บังคับ)' : ' (ไม่บังคับ)'}
        </label>
        <input
          id={hoursLostId}
          type="number"
          step="0.25"
          min="0"
          max="24"
          disabled={disabled}
          value={values.hoursLost}
          onChange={(e) => onChange({ hoursLost: e.target.value })}
          className={`text-right ${inputClass}`}
        />
      </div>

      <div className="col-span-2">
        <ActivityIdChips
          activityIds={values.activityIds}
          onChange={(activityIds) => onChange({ activityIds })}
          disabled={disabled}
        />
      </div>
    </div>
  )
}
