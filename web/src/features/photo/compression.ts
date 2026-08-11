import { computeOrientationTransform } from './orientationTransform'
import { parseJpegOrientation, stripJpegOrientationTag } from './imageOrientation'

/** S12-FE-01 DoD: "รูปถูกบีบอัดฝั่ง client ≤ ~300 KB". */
export const PHOTO_MAX_BYTES = 300 * 1024
/** Long-edge cap in CSS pixels — generous enough for a construction-site photo review, small enough
 * to make the compression loop below converge quickly on a typical 12+ MP phone camera image. */
export const PHOTO_MAX_DIMENSION = 1920
export const PHOTO_INITIAL_QUALITY = 0.9
export const PHOTO_MIN_QUALITY = 0.4
export const PHOTO_QUALITY_STEP = 0.1

/**
 * Pure step function for the "keep re-encoding at a lower quality until under the size target"
 * loop — returns the next JPEG quality to try, or `null` when the loop should stop (either the
 * target is already met, or quality has hit its floor and further reduction is not worth the visual
 * loss; `compressPhotoFile` accepts whatever the floor produced rather than looping forever).
 */
export function nextCompressionQuality(
  currentQuality: number,
  currentSizeBytes: number,
  targetBytes: number = PHOTO_MAX_BYTES,
): number | null {
  if (currentSizeBytes <= targetBytes) return null
  if (currentQuality <= PHOTO_MIN_QUALITY) return null
  return Math.max(PHOTO_MIN_QUALITY, Number((currentQuality - PHOTO_QUALITY_STEP).toFixed(2)))
}

export interface CompressedPhoto {
  blob: Blob
  width: number
  height: number
  /** The EXIF orientation this run detected and baked into the output pixels (1 = none needed). */
  orientationApplied: number
}

function canvasToBlob(canvas: HTMLCanvasElement, type: string, quality: number): Promise<Blob> {
  return new Promise((resolve, reject) => {
    canvas.toBlob(
      (blob) => (blob ? resolve(blob) : reject(new Error('สร้างไฟล์ภาพที่บีบอัดแล้วไม่สำเร็จ'))),
      type,
      quality,
    )
  })
}

/**
 * `imageOrientation: 'none'` is requested here as defense-in-depth/documentation of intent, but it is
 * NOT what actually guarantees an un-rotated decode — a real-browser E2E run (S12-QA-01) proved it is
 * not honoured reliably (Chromium 151 rotated an orientation-6 JPEG's pixels despite `'none'`, making
 * this module's own orientation transform double-apply on top of the browser's). What actually makes
 * this safe is that `buffer` has already had its Orientation tag neutralised to `1` by
 * `stripJpegOrientationTag` before it ever reaches here (see `compressPhotoFile` below and
 * `imageOrientation.ts`'s own remarks) — with the tag reading `1`, every orientation-handling mode
 * decodes identically to "no rotation", so which mode the engine actually implements stops mattering.
 */
async function loadBitmap(blob: Blob): Promise<ImageBitmap> {
  return createImageBitmap(blob, { imageOrientation: 'none' })
}

/**
 * Orchestrates decode -> bake EXIF orientation into upright pixels -> downscale -> re-encode JPEG
 * under the size budget (S12-FE-01 DoD + the backend's own flagged constraint —
 * `CMPlus.Application.Photos.ExifScrubber`'s remarks: "the backend strips EXIF including
 * Orientation... flagged for the frontend to bake orientation into pixels client-side before
 * upload"). The output is a fresh canvas re-encode, so it carries **no EXIF segment at all** — the
 * orientation is physically in the pixels, which is exactly what survives the backend's own
 * unconditional EXIF strip.
 *
 * **Not unit-tested directly** — `canvas.getContext('2d')`/`createImageBitmap` do not exist in this
 * project's jsdom test environment (confirmed: this repo's own `npm test` output already logs "Not
 * implemented: HTMLCanvasElement's getContext()" from unrelated chart components). Every piece of
 * genuinely trick logic this function calls into — EXIF parsing (`imageOrientation.test.ts`), the
 * orientation-to-transform math (`orientationTransform.test.ts`), and the compression step function
 * (`compression.test.ts`, below) — is unit-tested directly; this function is the thin glue and is the
 * one piece of S12-FE-01 that is only exercised by a real browser (`qa-engineer`'s S12-QA-01 offline
 * E2E suite, per the task brief).
 */
export async function compressPhotoFile(file: File): Promise<CompressedPhoto> {
  const buffer = await file.arrayBuffer()
  const orientation = file.type === 'image/jpeg' ? parseJpegOrientation(buffer) : 1
  // Decode from a copy whose Orientation tag reads `1` — see `loadBitmap`'s and
  // `imageOrientation.ts`'s remarks on why this, not the `imageOrientation: 'none'` decode option
  // alone, is what actually guarantees `bitmap.width`/`.height` are the raw, un-rotated dimensions
  // that `computeOrientationTransform` below expects to receive.
  const decodableBuffer = orientation === 1 ? buffer : stripJpegOrientationTag(buffer)

  const bitmap = await loadBitmap(new Blob([decodableBuffer], { type: file.type }))
  const { canvasWidth, canvasHeight, matrix } = computeOrientationTransform(orientation, bitmap.width, bitmap.height)

  const scale = Math.min(1, PHOTO_MAX_DIMENSION / Math.max(canvasWidth, canvasHeight))
  const outputWidth = Math.max(1, Math.round(canvasWidth * scale))
  const outputHeight = Math.max(1, Math.round(canvasHeight * scale))

  const canvas = document.createElement('canvas')
  canvas.width = outputWidth
  canvas.height = outputHeight
  const ctx = canvas.getContext('2d')
  if (!ctx) throw new Error('อุปกรณ์นี้ไม่รองรับการประมวลผลภาพ (canvas 2D context ไม่พร้อมใช้งาน)')

  ctx.save()
  ctx.scale(scale, scale)
  ctx.transform(...matrix)
  ctx.drawImage(bitmap, 0, 0)
  ctx.restore()
  bitmap.close()

  let quality = PHOTO_INITIAL_QUALITY
  let blob = await canvasToBlob(canvas, 'image/jpeg', quality)

  for (;;) {
    const next = nextCompressionQuality(quality, blob.size)
    if (next === null) break
    quality = next
    blob = await canvasToBlob(canvas, 'image/jpeg', quality)
  }

  return { blob, width: outputWidth, height: outputHeight, orientationApplied: orientation }
}
