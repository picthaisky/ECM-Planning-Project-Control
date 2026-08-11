import { describe, expect, it } from 'vitest'
import { parseJpegOrientation, stripJpegOrientationTag } from './imageOrientation'

/**
 * Hand-builds a minimal JPEG byte sequence: SOI, one APP1 segment carrying a one-entry EXIF IFD0
 * (just the Orientation tag), EOI. Every offset here is cross-checked against
 * `imageOrientation.ts#readExifOrientationFromApp1`'s own reads (tiffStart = APP1 payload start + 6,
 * IFD0 at tiffStart + firstIfdOffset, each entry 12 bytes) so a parser regression that shifts an
 * offset by even one byte fails these tests, not just "looks plausible".
 */
function buildJpegWithOrientation(orientation: number, littleEndian = true): ArrayBuffer {
  const exifHeader = [0x45, 0x78, 0x69, 0x66, 0x00, 0x00] // "Exif\0\0"
  const byteOrderMark = littleEndian ? [0x49, 0x49] : [0x4d, 0x4d]
  const magic = littleEndian ? [0x2a, 0x00] : [0x00, 0x2a]
  const firstIfdOffset = littleEndian ? [0x08, 0x00, 0x00, 0x00] : [0x00, 0x00, 0x00, 0x08]

  const entryCount = littleEndian ? [0x01, 0x00] : [0x00, 0x01]
  const tag = littleEndian ? [0x12, 0x01] : [0x01, 0x12] // 0x0112 Orientation
  const type = littleEndian ? [0x03, 0x00] : [0x00, 0x03] // SHORT
  const count = littleEndian ? [0x01, 0x00, 0x00, 0x00] : [0x00, 0x00, 0x00, 0x01]
  // A SHORT value is left-justified within the 4-byte value field, in the TIFF's own byte order.
  const value = littleEndian ? [orientation, 0x00, 0x00, 0x00] : [0x00, orientation, 0x00, 0x00]
  const nextIfdOffset = [0x00, 0x00, 0x00, 0x00]

  const tiff = [...byteOrderMark, ...magic, ...firstIfdOffset]
  const ifd0 = [...entryCount, ...tag, ...type, ...count, ...value, ...nextIfdOffset]
  const app1Payload = [...exifHeader, ...tiff, ...ifd0]
  const app1Length = app1Payload.length + 2 // JPEG segment length includes its own 2 length bytes
  const app1LengthBytes = [(app1Length >> 8) & 0xff, app1Length & 0xff] // marker length is always big-endian

  const bytes = [0xff, 0xd8, 0xff, 0xe1, ...app1LengthBytes, ...app1Payload, 0xff, 0xd9]
  return new Uint8Array(bytes).buffer
}

describe('parseJpegOrientation', () => {
  it.each([1, 2, 3, 4, 5, 6, 7, 8])('reads little-endian ("II") orientation %i correctly', (orientation) => {
    expect(parseJpegOrientation(buildJpegWithOrientation(orientation, true))).toBe(orientation)
  })

  it.each([1, 3, 6, 8])('reads big-endian ("MM") orientation %i correctly', (orientation) => {
    expect(parseJpegOrientation(buildJpegWithOrientation(orientation, false))).toBe(orientation)
  })

  it('defaults to 1 (normal) for a JPEG with no APP1/EXIF segment at all', () => {
    const bytes = new Uint8Array([0xff, 0xd8, 0xff, 0xd9]) // SOI, EOI only
    expect(parseJpegOrientation(bytes.buffer)).toBe(1)
  })

  it('defaults to 1 for a non-JPEG buffer (e.g. a PNG signature)', () => {
    const png = new Uint8Array([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a])
    expect(parseJpegOrientation(png.buffer)).toBe(1)
  })

  it('defaults to 1 for an empty/truncated buffer without throwing', () => {
    expect(parseJpegOrientation(new Uint8Array([0xff]).buffer)).toBe(1)
    expect(parseJpegOrientation(new Uint8Array([]).buffer)).toBe(1)
  })

  it('defaults to 1 when an APP1 segment exists but is not an EXIF segment (e.g. XMP)', () => {
    const xmpMagic = Array.from('http://ns.adobe.com/xap/1.0/\0').map((c) => c.charCodeAt(0))
    const length = xmpMagic.length + 2
    const lengthBytes = [(length >> 8) & 0xff, length & 0xff]
    const bytes = new Uint8Array([0xff, 0xd8, 0xff, 0xe1, ...lengthBytes, ...xmpMagic, 0xff, 0xd9])
    expect(parseJpegOrientation(bytes.buffer)).toBe(1)
  })
})

/**
 * S12-QA-01 (defect 2) regression coverage: `createImageBitmap`'s `imageOrientation: 'none'` option
 * was found, via a real-browser E2E run, not to reliably suppress EXIF-based rotation (see
 * `imageOrientation.ts`'s own top-of-file remarks and `compression.ts`'s `loadBitmap`). This function
 * is what makes the fix work regardless of that browser inconsistency — its own logic is fully
 * unit-testable (pure byte manipulation, no canvas/`createImageBitmap` involved).
 */
describe('stripJpegOrientationTag', () => {
  it.each([1, 2, 3, 4, 5, 6, 7, 8])('forces orientation %i to read as 1 afterwards (little-endian)', (orientation) => {
    const original = buildJpegWithOrientation(orientation, true)
    const stripped = stripJpegOrientationTag(original)

    expect(parseJpegOrientation(stripped)).toBe(1)
    // The original buffer itself must be untouched — callers may still need the real orientation
    // value from it (`compression.ts` parses it *before* calling this).
    expect(parseJpegOrientation(original)).toBe(orientation)
  })

  it.each([1, 3, 6, 8])('forces orientation %i to read as 1 afterwards (big-endian)', (orientation) => {
    const original = buildJpegWithOrientation(orientation, false)
    const stripped = stripJpegOrientationTag(original)

    expect(parseJpegOrientation(stripped)).toBe(1)
    expect(parseJpegOrientation(original)).toBe(orientation)
  })

  it('returns the exact same buffer reference (no copy) when there is nothing to strip', () => {
    const bytes = new Uint8Array([0xff, 0xd8, 0xff, 0xd9]).buffer // SOI, EOI, no APP1 at all
    expect(stripJpegOrientationTag(bytes)).toBe(bytes)
  })

  it('leaves every other byte in the file identical — only the 2-byte orientation value changes', () => {
    const original = buildJpegWithOrientation(6, true)
    const stripped = stripJpegOrientationTag(original)

    const originalBytes = new Uint8Array(original)
    const strippedBytes = new Uint8Array(stripped)
    expect(strippedBytes.length).toBe(originalBytes.length)

    let diffCount = 0
    for (let i = 0; i < originalBytes.length; i++) {
      if (originalBytes[i] !== strippedBytes[i]) diffCount++
    }
    // Orientation 6 (0x0006) vs 1 (0x0001) differs in exactly one byte in little-endian SHORT
    // encoding (the low byte; the high byte is 0x00 in both).
    expect(diffCount).toBe(1)
  })

  it('is a safe no-op (returns the input unchanged) for a non-JPEG or malformed buffer', () => {
    const png = new Uint8Array([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]).buffer
    expect(stripJpegOrientationTag(png)).toBe(png)

    const truncated = new Uint8Array([0xff]).buffer
    expect(stripJpegOrientationTag(truncated)).toBe(truncated)
  })
})
