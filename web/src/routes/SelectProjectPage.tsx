import { useId, useState } from 'react'
import type { FormEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import { Button } from '../components'
import { useProjectStore } from '../store/projectStore'

const GUID_PATTERN =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

/**
 * Interim project-selection gate (S4-FE-01) — not one of the 13 nav screens, just the seam that
 * lets a user pick which project's `/app/:projectId/*` shell to enter until a real project-list
 * endpoint exists (see `store/projectStore.ts`'s remarks for why this is necessary this sprint).
 * Every real screen keeps working against the real API the moment that endpoint ships — only this
 * page's manual-entry step goes away, replaced by a proper picker.
 */
export function SelectProjectPage() {
  const inputId = useId()
  const [value, setValue] = useState('')
  const [error, setError] = useState<string | null>(null)
  const navigate = useNavigate()
  const setCurrentProjectId = useProjectStore((state) => state.setCurrentProjectId)

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const trimmed = value.trim()

    if (!GUID_PATTERN.test(trimmed)) {
      setError('รหัสโครงการต้องอยู่ในรูปแบบ GUID เช่น 3fa85f64-5717-4562-b3fc-2c963f66afa6')
      return
    }

    setError(null)
    setCurrentProjectId(trimmed)
    navigate(`/app/${trimmed}/dashboard`, { replace: true })
  }

  return (
    <div className="flex min-h-screen items-center justify-center bg-bg px-4">
      <form
        onSubmit={handleSubmit}
        noValidate
        className="w-full max-w-md rounded-card border border-border bg-surface p-8"
      >
        <h1 className="font-heading text-xl font-bold text-navy">เลือกโครงการ</h1>
        <p className="mt-1 text-sm text-text-muted">
          กรุณาระบุรหัสอ้างอิงโครงการ (Project ID) เพื่อเข้าใช้งาน
        </p>

        <label htmlFor={inputId} className="mt-6 block text-xs font-medium text-text-muted">
          รหัสโครงการ (Project ID)
        </label>
        <input
          id={inputId}
          type="text"
          placeholder="3fa85f64-5717-4562-b3fc-2c963f66afa6"
          value={value}
          onChange={(event) => setValue(event.target.value)}
          className="mt-1 w-full rounded-card border border-border px-3 py-2 font-mono text-sm text-text focus:border-navy focus:outline-none"
        />

        {error && (
          <p role="alert" className="mt-3 text-xs text-danger">
            {error}
          </p>
        )}

        <Button type="submit" className="mt-6 w-full">
          เข้าสู่โครงการ
        </Button>
      </form>
    </div>
  )
}
