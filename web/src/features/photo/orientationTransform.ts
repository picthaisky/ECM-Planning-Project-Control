/**
 * Pure geometry: EXIF orientation value (1-8) + source pixel dimensions -> the 2D canvas transform
 * that draws the image physically upright, plus the output canvas dimensions (swapped for the four
 * orientations that involve a 90°/270° rotation). Matrix values are the standard `ctx.transform(a, b,
 * c, d, e, f)` recipe used by every major hand-rolled EXIF-orientation image library (e.g.
 * blueimp-load-image, the reference most browser-vendor bug reports on this topic cite) — verified
 * against the EXIF 2.3 orientation table (1 = normal ... 8 = rotate 270° CW), not derived ad hoc.
 *
 * No DOM/canvas dependency — pure numbers in, pure numbers out — so every one of the 8 orientation
 * cases is directly unit-testable, unlike the canvas-drawing code that consumes this
 * (`compression.ts#compressPhotoFile`, which jsdom cannot exercise — see that file's own remarks).
 */

export interface OrientationTransform {
  canvasWidth: number
  canvasHeight: number
  /** `[a, b, c, d, e, f]` for `CanvasRenderingContext2D.transform(a, b, c, d, e, f)`, applied before
   * `drawImage(source, 0, 0)`. */
  matrix: [number, number, number, number, number, number]
}

const IDENTITY: [number, number, number, number, number, number] = [1, 0, 0, 1, 0, 0]

/** `orientation` outside 1-8 is treated as 1 (normal) — the same "fail soft to no correction"
 * posture `imageOrientation.ts#parseJpegOrientation` uses for a missing/malformed tag. */
export function computeOrientationTransform(
  orientation: number,
  sourceWidth: number,
  sourceHeight: number,
): OrientationTransform {
  const swapsDimensions = orientation >= 5 && orientation <= 8

  const matrices: Record<number, [number, number, number, number, number, number]> = {
    1: IDENTITY,
    2: [-1, 0, 0, 1, sourceWidth, 0], // flip horizontal
    3: [-1, 0, 0, -1, sourceWidth, sourceHeight], // rotate 180
    4: [1, 0, 0, -1, 0, sourceHeight], // flip vertical
    5: [0, 1, 1, 0, 0, 0], // transpose
    6: [0, 1, -1, 0, sourceHeight, 0], // rotate 90 CW
    7: [0, -1, -1, 0, sourceHeight, sourceWidth], // transverse
    8: [0, -1, 1, 0, 0, sourceWidth], // rotate 270 CW (90 CCW)
  }

  return {
    canvasWidth: swapsDimensions ? sourceHeight : sourceWidth,
    canvasHeight: swapsDimensions ? sourceWidth : sourceHeight,
    matrix: matrices[orientation] ?? IDENTITY,
  }
}
