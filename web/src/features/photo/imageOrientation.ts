/**
 * Hand-rolled EXIF Orientation tag reader (S12-FE-01). Pure — takes raw JPEG bytes, returns the
 * orientation value (1-8, default 1 = "normal, no correction needed") with no DOM/canvas/Image
 * dependency, so it is fully unit-testable in this project's jsdom test environment (which has
 * neither `HTMLCanvasElement.getContext` nor `createImageBitmap` — confirmed by this repo's own
 * `npm test` output warning "Not implemented: HTMLCanvasElement's getContext()").
 *
 * Deliberately **not** delegated to `createImageBitmap(file, { imageOrientation: 'from-image' })`
 * even though modern engines support it: that option's *default* value has shipped inconsistently
 * across browsers historically — a real, documented cross-browser inconsistency this app does not
 * want to depend on silently doing the right thing. Reading the tag ourselves and applying our own
 * transform (`orientationTransform.ts`) makes the behaviour explicit and independently testable end
 * to end.
 *
 * `stripJpegOrientationTag` (S12-QA-01 fix) exists for the same reason, sharpened by an empirical
 * finding: `createImageBitmap(file, { imageOrientation: 'none' })` was found, in this project's own
 * real-browser E2E suite, to **not** actually suppress EXIF-based rotation for an orientation-6 JPEG
 * (Chromium 151, confirmed by direct experiment — `bitmap.width`/`.height` came back already rotated
 * despite the explicit `'none'` request). Passing `'none'` and trusting it is therefore not a safe
 * cross-browser (or even cross-version) way to get "give me the raw, un-rotated pixels" — the only
 * way to *guarantee* that, on any engine, is to make the question moot: physically zero out the
 * Orientation tag's stored value (to `1`, "normal") in a byte copy before decoding. With the tag
 * reading `1`, `'from-image'` and `'none'` decode identically (there is nothing left to apply), so
 * `compression.ts` gets the true raw pixel layout regardless of which behaviour the engine actually
 * implements — and can then safely apply its own, independently-tested transform on top.
 */

const JPEG_SOI = 0xffd8
const APP1_MARKER = 0xe1
const EXIF_ORIENTATION_TAG = 0x0112

interface OrientationTagLocation {
  /** Absolute byte offset, within the buffer that was walked, of the 2-byte orientation SHORT value
   * (i.e. the IFD entry's value field — Orientation is always inline, never an offset, since a SHORT
   * fits in the 4-byte value field). */
  valueOffset: number
  littleEndian: boolean
}

/**
 * Walks a JPEG's marker segments looking for an APP1/EXIF segment carrying IFD0's Orientation tag
 * (0x0112), returning exactly *where* its 2-byte value lives rather than the value itself — shared by
 * `parseJpegOrientation` (reads it) and `stripJpegOrientationTag` (overwrites it) so the marker-walk /
 * TIFF-parsing logic exists exactly once. Returns `null` for anything this parser cannot safely
 * interpret — both callers fail soft to "treat as orientation 1 / nothing to strip" in that case,
 * since a photo with no orientation info needs no correction by definition.
 */
function locateOrientationTag(buffer: ArrayBufferLike): OrientationTagLocation | null {
  try {
    const view = new DataView(buffer)
    if (view.byteLength < 4 || view.getUint16(0) !== JPEG_SOI) return null

    let offset = 2
    while (offset < view.byteLength - 1) {
      if (view.getUint8(offset) !== 0xff) return null

      let markerOffset = offset + 1
      // Any number of 0xFF fill bytes may legally precede the real marker code.
      while (markerOffset < view.byteLength && view.getUint8(markerOffset) === 0xff) markerOffset++
      if (markerOffset >= view.byteLength) return null

      const marker = view.getUint8(markerOffset)
      const segmentStart = markerOffset + 1

      // SOS (start of scan): entropy-coded data follows, no more markers to inspect.
      if (marker === 0xda) return null
      if (segmentStart + 2 > view.byteLength) return null

      const segmentLength = view.getUint16(segmentStart)
      if (segmentLength < 2 || segmentStart + segmentLength > view.byteLength) return null

      if (marker === APP1_MARKER) {
        const location = locateOrientationInApp1(view, segmentStart + 2, segmentLength - 2)
        if (location) return location
      }

      offset = segmentStart + segmentLength
    }
    return null
  } catch {
    return null
  }
}

function locateOrientationInApp1(view: DataView, start: number, length: number): OrientationTagLocation | null {
  // "Exif\0\0" header (6 bytes) precedes the TIFF structure.
  if (length < 8) return null
  if (
    view.getUint32(start) !== 0x45786966 || // 'Exif'
    view.getUint16(start + 4) !== 0x0000
  ) {
    return null
  }

  const tiffStart = start + 6
  const byteOrderMark = view.getUint16(tiffStart)
  const littleEndian = byteOrderMark === 0x4949 // 'II'
  if (!littleEndian && byteOrderMark !== 0x4d4d) return null // not 'MM' either -> not valid TIFF

  const get16 = (o: number) => view.getUint16(o, littleEndian)
  const get32 = (o: number) => view.getUint32(o, littleEndian)

  const firstIfdOffset = get32(tiffStart + 4)
  const ifdStart = tiffStart + firstIfdOffset
  if (ifdStart + 2 > view.byteLength) return null

  const entryCount = get16(ifdStart)
  for (let i = 0; i < entryCount; i++) {
    const entryOffset = ifdStart + 2 + i * 12
    if (entryOffset + 12 > view.byteLength) break
    if (get16(entryOffset) === EXIF_ORIENTATION_TAG) {
      return { valueOffset: entryOffset + 8, littleEndian }
    }
  }
  return null
}

/** Returns the EXIF Orientation value (1-8), or `1` ("normal") when the file is not a JPEG, carries
 * no EXIF APP1 segment, or the segment is malformed in any way this parser cannot safely interpret —
 * fails soft to "no correction" rather than throwing, since a photo with no orientation info needs no
 * correction by definition. Also normalises an out-of-range stored value (e.g. `0`) to `1` for the
 * same reason. */
export function parseJpegOrientation(buffer: ArrayBufferLike): number {
  const location = locateOrientationTag(buffer)
  if (!location) return 1
  try {
    const value = new DataView(buffer).getUint16(location.valueOffset, location.littleEndian)
    return value >= 1 && value <= 8 ? value : 1
  } catch {
    return 1
  }
}

/**
 * Returns a byte-identical copy of `buffer` with the EXIF Orientation tag's stored value forced to
 * `1` ("normal"), if one is present — otherwise returns `buffer` unchanged (same reference, no copy
 * made, since there is nothing to neutralise). See this file's own top-of-file remarks for why this
 * exists: it is what actually makes "decode without the browser applying its own EXIF rotation"
 * reliable, since the `createImageBitmap` `imageOrientation: 'none'` option alone was found not to be
 * trustworthy for that. Always call `parseJpegOrientation` on the *original* buffer first to know
 * which transform to apply yourself — this function's whole point is that its *output* buffer must be
 * decoded as if orientation were `1` (i.e. no further browser-side rotation happens), regardless of
 * what the original tag said.
 */
export function stripJpegOrientationTag(buffer: ArrayBuffer): ArrayBuffer {
  const location = locateOrientationTag(buffer)
  if (!location) return buffer
  try {
    const copy = buffer.slice(0)
    new DataView(copy).setUint16(location.valueOffset, 1, location.littleEndian)
    return copy
  } catch {
    return buffer
  }
}
