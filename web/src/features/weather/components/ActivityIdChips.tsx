import { useState } from 'react'
import type { FormEvent } from 'react'
import { Button } from '../../../components'

export interface ActivityIdChipsProps {
  activityIds: string[]
  onChange: (activityIds: string[]) => void
  disabled?: boolean
}

/** Mirrors `features/wbs/useBatchProgressForm.ts#GUID_PATTERN` exactly — same format, same error
 * copy — since this is the identical underlying problem: there is no live endpoint anywhere in this
 * codebase that lists a project's activities to pick from (`features/wbs/api.ts#getNodeActivities`'s
 * own remarks: the closest one is 404 on the real backend today, and it is scoped per-WBS-node, not
 * project-wide). `AffectedActivityIds` on a weather log has the same shape and the same gap, so it
 * gets the same, already-proven answer rather than a new one. */
const GUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

/**
 * Manual "เพิ่มกิจกรรมด้วยรหัส (Activity ID)" chip list for `DailyWeatherLog.AffectedActivityIds`
 * (domain-rules.md weather-eot §3.2) — optional (an unattributed stoppage is valid evidence, just
 * not yet evaluable, §3.2/fixture W-09), so this control never forces at least one chip.
 */
export function ActivityIdChips({ activityIds, onChange, disabled }: ActivityIdChipsProps) {
  const [draft, setDraft] = useState('')
  const [error, setError] = useState<string | null>(null)

  function handleAdd(event: FormEvent) {
    event.preventDefault()
    const trimmed = draft.trim()
    if (!GUID_PATTERN.test(trimmed)) {
      setError('รหัสกิจกรรมต้องอยู่ในรูปแบบ GUID เช่น 3fa85f64-5717-4562-b3fc-2c963f66afa6')
      return
    }
    if (activityIds.includes(trimmed)) {
      setError('กิจกรรมนี้อยู่ในรายการแล้ว')
      return
    }
    onChange([...activityIds, trimmed])
    setDraft('')
    setError(null)
  }

  function handleRemove(id: string) {
    onChange(activityIds.filter((existing) => existing !== id))
  }

  return (
    <div>
      <label htmlFor="activity-id-draft" className="block text-[11px] text-text-faint">
        กิจกรรมที่ได้รับผลกระทบ (Activity ID) — ไม่บังคับ
      </label>
      <form onSubmit={handleAdd} className="mt-1 flex gap-2">
        <input
          id="activity-id-draft"
          type="text"
          disabled={disabled}
          placeholder="3fa85f64-5717-4562-b3fc-2c963f66afa6"
          value={draft}
          onChange={(e) => {
            setDraft(e.target.value)
            setError(null)
          }}
          className="w-full flex-1 rounded-card border border-border px-2.5 py-1.5 font-mono text-xs text-text focus:border-navy focus:outline-none disabled:bg-bg disabled:text-text-faint"
        />
        <Button type="submit" size="sm" variant="secondary" disabled={disabled}>
          + เพิ่ม
        </Button>
      </form>
      {error && (
        <p role="alert" className="mt-1 text-[10.5px] text-danger">
          {error}
        </p>
      )}

      {activityIds.length === 0 ? (
        <p className="mt-2 text-[10.5px] text-text-faint">
          ยังไม่ได้ระบุกิจกรรม — บันทึกได้ปกติ แต่จะไม่ถูกนับในการประเมิน EOT จนกว่าจะระบุกิจกรรม (ยื่นบันทึกแก้ไขภายหลังได้)
        </p>
      ) : (
        <ul className="mt-2 flex flex-wrap gap-1.5">
          {activityIds.map((id) => (
            <li
              key={id}
              className="flex items-center gap-1.5 rounded bg-bg px-2 py-1 font-mono text-[10.5px] text-text"
            >
              {id}
              {!disabled && (
                <button
                  type="button"
                  aria-label={`ลบกิจกรรม ${id}`}
                  onClick={() => handleRemove(id)}
                  className="text-danger hover:text-danger/70"
                >
                  &times;
                </button>
              )}
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
