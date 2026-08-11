import { useId, useRef, useState } from 'react'
import { Button } from '../../../components'
import { emptyPhotoCaptureFormValues, validatePhotoCaptureFormValues } from '../photoForm'
import type { PhotoCaptureFormValues } from '../photoForm'
import type { UploadPhotoFields } from '../types'

export interface PhotoCaptureFormProps {
  onCapture: (file: File, fields: UploadPhotoFields) => Promise<boolean>
  busy: boolean
  errorMessage: string | null
}

/**
 * S12-FE-01's "+ อัปโหลดรูป" capture form (prototype line ~365). `accept="image/*"
 * capture="environment"` is a *hint*, not a restriction — on a phone it opens the rear camera
 * directly (the DoD's "ตัดเน็ตแล้วถ่ายรูป" flow); on desktop (or when the user taps "เลือกจากคลังภาพ"
 * inside the native picker) it falls back to an ordinary file chooser, so this one input serves both
 * "take a photo now" and "attach an existing one" without two separate controls.
 *
 * Compression + EXIF-orientation baking (`compression.ts`) and the outbox enqueue both happen inside
 * `onCapture` (`usePhotoOutbox.ts#capture`) — this component only collects the file + the two
 * optional fields and hands off.
 */
export function PhotoCaptureForm({ onCapture, busy, errorMessage }: PhotoCaptureFormProps) {
  const [values, setValues] = useState<PhotoCaptureFormValues>(emptyPhotoCaptureFormValues)
  const [validationError, setValidationError] = useState<string | null>(null)
  const [selectedFile, setSelectedFile] = useState<File | null>(null)
  const fileInputRef = useRef<HTMLInputElement>(null)
  const activityIdInputId = useId()
  const captionInputId = useId()

  function resetSelection() {
    setSelectedFile(null)
    if (fileInputRef.current) fileInputRef.current.value = ''
  }

  async function handleSubmit() {
    if (busy) return
    if (!selectedFile) {
      setValidationError('กรุณาถ่ายหรือเลือกรูปภาพก่อน')
      return
    }
    const validation = validatePhotoCaptureFormValues(values)
    setValidationError(validation)
    if (validation) return

    const succeeded = await onCapture(selectedFile, {
      activityId: values.activityId.trim() === '' ? null : values.activityId.trim(),
      caption: values.caption.trim() === '' ? null : values.caption.trim(),
      capturedAt: new Date().toISOString(),
    })
    if (succeeded) {
      setValues(emptyPhotoCaptureFormValues())
      resetSelection()
    }
  }

  return (
    <div className="rounded-card border border-border bg-surface p-4">
      <h3 className="font-heading text-sm font-semibold text-navy">ถ่าย/แนบรูปความคืบหน้าหน้างาน</h3>

      <div className="mt-3 grid grid-cols-1 gap-3 sm:grid-cols-2">
        <div className="sm:col-span-2">
          <label htmlFor="photo-file-input" className="block text-[11px] text-text-faint">
            รูปภาพ
          </label>
          <input
            id="photo-file-input"
            ref={fileInputRef}
            type="file"
            accept="image/*"
            capture="environment"
            disabled={busy}
            onChange={(e) => setSelectedFile(e.target.files?.[0] ?? null)}
            className="mt-1 w-full text-xs text-text file:mr-3 file:rounded-card file:border file:border-navy file:bg-surface file:px-3 file:py-1.5 file:text-xs file:font-medium file:text-navy"
          />
          {selectedFile && <p className="mt-1 text-[10.5px] text-text-faint">เลือกแล้ว: {selectedFile.name}</p>}
        </div>

        <div>
          <label htmlFor={activityIdInputId} className="block text-[11px] text-text-faint">
            ผูกกับกิจกรรม (Activity ID) — ไม่บังคับ
          </label>
          <input
            id={activityIdInputId}
            type="text"
            disabled={busy}
            placeholder="3fa85f64-5717-4562-b3fc-2c963f66afa6"
            value={values.activityId}
            onChange={(e) => setValues((prev) => ({ ...prev, activityId: e.target.value }))}
            className="mt-1 w-full rounded-card border border-border px-2.5 py-1.5 font-mono text-xs text-text focus:border-navy focus:outline-none disabled:bg-bg disabled:text-text-faint"
          />
        </div>

        <div>
          <label htmlFor={captionInputId} className="block text-[11px] text-text-faint">
            คำอธิบายภาพ — ไม่บังคับ
          </label>
          <input
            id={captionInputId}
            type="text"
            disabled={busy}
            maxLength={500}
            placeholder='เช่น "เทคอนกรีตพื้นชั้น 9"'
            value={values.caption}
            onChange={(e) => setValues((prev) => ({ ...prev, caption: e.target.value }))}
            className="mt-1 w-full rounded-card border border-border px-2.5 py-1.5 text-xs text-text focus:border-navy focus:outline-none disabled:bg-bg disabled:text-text-faint"
          />
        </div>
      </div>

      {validationError && (
        <p role="alert" className="mt-3 text-xs text-danger">
          {validationError}
        </p>
      )}
      {errorMessage && (
        <p role="alert" className="mt-3 text-xs text-danger">
          {errorMessage}
        </p>
      )}

      <div className="mt-3 flex justify-end">
        <Button size="sm" loading={busy} onClick={() => void handleSubmit()}>
          {busy ? 'กำลังบีบอัดรูปภาพ...' : '+ บันทึกรูปภาพ (เข้าคิวออฟไลน์)'}
        </Button>
      </div>
    </div>
  )
}
