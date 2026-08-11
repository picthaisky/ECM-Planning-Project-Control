/** All-string/controlled capture form state — mirrors `features/weather/weatherForm.ts`'s
 * established "all-string FormValues, parsed only at submit" pattern. */
export interface PhotoCaptureFormValues {
  activityId: string
  caption: string
}

export function emptyPhotoCaptureFormValues(): PhotoCaptureFormValues {
  return { activityId: '', caption: '' }
}

/** A GUID-shaped check only (matches the pattern every other feature folder in this codebase already
 * uses for a client-typed reference id — e.g. `features/weather/components/ActivityIdChips.tsx`) —
 * the server is always the real authority (`PhotoErrorCodes.UnknownActivity`). */
const GUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

export function validatePhotoCaptureFormValues(values: PhotoCaptureFormValues): string | null {
  if (values.activityId.trim() !== '' && !GUID_PATTERN.test(values.activityId.trim())) {
    return 'รหัสกิจกรรม (Activity ID) ต้องอยู่ในรูปแบบ GUID เช่น 3fa85f64-5717-4562-b3fc-2c963f66afa6'
  }
  if (values.caption.length > 500) {
    return 'คำอธิบายภาพต้องไม่เกิน 500 ตัวอักษร'
  }
  return null
}
