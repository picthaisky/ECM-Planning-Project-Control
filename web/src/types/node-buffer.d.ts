/**
 * Minimal ambient typing for `node:buffer`'s `Blob` export, scoped to exactly this one named export
 * — `tsconfig.app.json` deliberately restricts `types` to `["vite/client"]` only (this is a browser
 * app; adding the full `"node"` ambient type package would leak Node globals — `process`, `Buffer`,
 * etc. — into application code's autocomplete/type space for the sake of a test-only need).
 *
 * Why this is needed at all: `services/outbox/storage.test.ts` and
 * `features/photo/usePhotoOutbox.test.ts` construct Blobs with Node's *native* `Blob` (via
 * `import { Blob as NodeBlob } from 'node:buffer'`), not jsdom's global `Blob` — confirmed by an
 * isolated repro (both files' own import comments) that jsdom's `Blob` is not a class Node's global
 * `structuredClone` (which `fake-indexeddb` clones stored values with) recognises, silently producing
 * `{}` instead of a real Blob. Node's native `Blob` is structurally the same interface, just
 * registered with V8's structured-clone serializer, and is what a real browser's own single-realm
 * engine would give you anyway — this whole file exists only to type-check that one import, not to
 * change what code the test actually exercises.
 */
declare module 'node:buffer' {
  const Blob: typeof globalThis.Blob
  export { Blob }
}
