import { describe, expect, it } from 'vitest'
import { computeOrientationTransform } from './orientationTransform'

describe('computeOrientationTransform', () => {
  it('orientation 1 (normal) is the identity transform with unchanged dimensions', () => {
    const result = computeOrientationTransform(1, 800, 600)
    expect(result).toEqual({ canvasWidth: 800, canvasHeight: 600, matrix: [1, 0, 0, 1, 0, 0] })
  })

  it('orientation 2 (flip horizontal) keeps dimensions, mirrors on x', () => {
    const result = computeOrientationTransform(2, 800, 600)
    expect(result.canvasWidth).toBe(800)
    expect(result.canvasHeight).toBe(600)
    expect(result.matrix).toEqual([-1, 0, 0, 1, 800, 0])
  })

  it('orientation 3 (rotate 180) keeps dimensions', () => {
    const result = computeOrientationTransform(3, 800, 600)
    expect(result.canvasWidth).toBe(800)
    expect(result.canvasHeight).toBe(600)
    expect(result.matrix).toEqual([-1, 0, 0, -1, 800, 600])
  })

  it('orientation 4 (flip vertical) keeps dimensions', () => {
    const result = computeOrientationTransform(4, 800, 600)
    expect(result.canvasWidth).toBe(800)
    expect(result.canvasHeight).toBe(600)
    expect(result.matrix).toEqual([1, 0, 0, -1, 0, 600])
  })

  it.each([5, 6, 7, 8])('orientation %i swaps canvas width/height (90°-family rotation)', (orientation) => {
    const result = computeOrientationTransform(orientation, 800, 600)
    expect(result.canvasWidth).toBe(600)
    expect(result.canvasHeight).toBe(800)
  })

  it('orientation 6 (rotate 90 CW) is the common "phone held portrait, sensor landscape" case', () => {
    const result = computeOrientationTransform(6, 800, 600)
    expect(result.matrix).toEqual([0, 1, -1, 0, 600, 0])
  })

  it('orientation 8 (rotate 270 CW / 90 CCW)', () => {
    const result = computeOrientationTransform(8, 800, 600)
    expect(result.matrix).toEqual([0, -1, 1, 0, 0, 800])
  })

  it('falls back to the identity transform for an out-of-range orientation value', () => {
    const result = computeOrientationTransform(99, 800, 600)
    expect(result).toEqual({ canvasWidth: 800, canvasHeight: 600, matrix: [1, 0, 0, 1, 0, 0] })
  })
})
