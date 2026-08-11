import { describe, expect, it } from 'vitest'
import { PHOTO_MAX_BYTES, PHOTO_MIN_QUALITY, nextCompressionQuality } from './compression'

describe('nextCompressionQuality', () => {
  it('returns null (stop) once the current size is already under the target', () => {
    expect(nextCompressionQuality(0.9, PHOTO_MAX_BYTES - 1)).toBeNull()
    expect(nextCompressionQuality(0.9, PHOTO_MAX_BYTES)).toBeNull()
  })

  it('steps quality down by 0.1 while still over target and above the floor', () => {
    expect(nextCompressionQuality(0.9, PHOTO_MAX_BYTES + 1)).toBe(0.8)
    expect(nextCompressionQuality(0.6, PHOTO_MAX_BYTES + 1)).toBeCloseTo(0.5, 5)
  })

  it('never steps below the quality floor', () => {
    expect(nextCompressionQuality(PHOTO_MIN_QUALITY + 0.05, 10_000_000)).toBe(PHOTO_MIN_QUALITY)
  })

  it('returns null (give up further reduction) once already at the floor', () => {
    expect(nextCompressionQuality(PHOTO_MIN_QUALITY, 10_000_000)).toBeNull()
  })

  it('accepts a custom target for testability', () => {
    expect(nextCompressionQuality(0.9, 100, 50)).toBe(0.8)
    expect(nextCompressionQuality(0.9, 40, 50)).toBeNull()
  })
})
