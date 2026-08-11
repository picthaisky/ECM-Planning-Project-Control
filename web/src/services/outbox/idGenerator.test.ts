import { afterEach, describe, expect, it } from 'vitest'
import { generateOutboxId } from './idGenerator'

const UUID_V4_SHAPE = /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i

/**
 * `delete crypto.randomUUID` would silently do nothing in Node (it is a `Crypto.prototype` method,
 * not an own property of the `crypto` instance — `delete` only ever removes own properties, so the
 * inherited method would keep resolving right through a `delete` and the "fallback" tests below
 * would quietly exercise the *primary* path instead of the one they claim to). Shadowing it with an
 * own `undefined` value via `defineProperty` is what actually makes `typeof crypto.randomUUID` read
 * `'undefined'` for the duration of the test.
 */
function withoutRandomUUID<T>(run: () => T): T {
  const original = crypto.randomUUID
  Object.defineProperty(crypto, 'randomUUID', { value: undefined, configurable: true, writable: true })
  try {
    return run()
  } finally {
    Object.defineProperty(crypto, 'randomUUID', { value: original, configurable: true, writable: true })
  }
}

describe('generateOutboxId', () => {
  afterEach(() => {
    expect(typeof crypto.randomUUID).toBe('function')
  })

  it('produces a UUID-v4-shaped string via crypto.randomUUID when available', () => {
    const id = generateOutboxId()
    expect(id).toMatch(UUID_V4_SHAPE)
  })

  it('produces distinct ids on successive calls', () => {
    const ids = new Set(Array.from({ length: 50 }, () => generateOutboxId()))
    expect(ids.size).toBe(50)
  })

  it('falls back to a UUID-v4-shaped id when crypto.randomUUID is unavailable (non-secure-context devices)', () => {
    withoutRandomUUID(() => {
      expect(typeof crypto.randomUUID).toBe('undefined')
      const id = generateOutboxId()
      expect(id).toMatch(UUID_V4_SHAPE)
    })
  })

  it('fallback generator still produces distinct ids across successive calls', () => {
    withoutRandomUUID(() => {
      const ids = new Set(Array.from({ length: 20 }, () => generateOutboxId()))
      expect(ids.size).toBe(20)
    })
  })
})
