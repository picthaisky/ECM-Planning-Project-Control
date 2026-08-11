/**
 * `crypto.randomUUID()` is available on every target this app ships to (all evergreen mobile/desktop
 * browsers, and Node 19+/jsdom for tests) — but it requires a secure context (HTTPS or localhost),
 * which local HTTP-only site testing rigs occasionally are not. The fallback is RFC-4122-*shaped*
 * (correct version/variant nibbles) but **not** cryptographically random — acceptable here because
 * this id is only ever a local IndexedDB primary key and the value sent as `Idempotency-Key`, never a
 * security token; a collision would only ever be caught by IndexedDB's own key uniqueness, which
 * `outboxStore.ts#enqueue` treats as impossible to hit in practice at this app's write volume.
 */
export function generateOutboxId(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID()
  }

  let seed = Date.now() + Math.random() * 1_000_000
  const template = 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'
  return template.replace(/[xy]/g, (character) => {
    const random = (seed + Math.random() * 16) % 16 | 0
    seed = Math.floor(seed / 16)
    const value = character === 'x' ? random : (random & 0x3) | 0x8
    return value.toString(16)
  })
}
