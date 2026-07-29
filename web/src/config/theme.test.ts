import { describe, expect, it } from 'vitest'
import { colors, fontFamily } from './theme'

describe('theme tokens (Phase 0 smoke test)', () => {
  it('exposes the navy/gold token set as valid hex colors', () => {
    const hex = /^#[0-9A-Fa-f]{6}$/
    for (const value of Object.values(colors)) {
      expect(value).toMatch(hex)
    }
  })

  it('never uses Inter as a font family', () => {
    const allFonts = [...fontFamily.body, ...fontFamily.heading].join(' ')
    expect(allFonts).not.toMatch(/Inter/i)
  })
})
