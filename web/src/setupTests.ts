import '@testing-library/jest-dom/vitest'
import { afterEach } from 'vitest'
import { cleanup } from '@testing-library/react'

// @testing-library/react's built-in auto-cleanup relies on a *global*
// `afterEach` (it does not import one) — this project's vitest config does
// not set `test.globals: true`, so it never registered on its own. Without
// this, every `render()` in a multi-test file leaves its DOM (and, for
// portal-based components like Modal, document.body) mounted for the next
// test, breaking isolation.
afterEach(() => {
  cleanup()
})

// jsdom (this project's vitest `environment`) implements neither
// `URL.createObjectURL` nor `URL.revokeObjectURL` at all — confirmed directly against the
// installed jsdom package (no `createObjectURL` reference anywhere in `node_modules/jsdom`), unlike
// e.g. `HTMLCanvasElement.getContext()`, which at least exists and logs a "not implemented" warning.
// Every real target browser implements both; this is a same-shape polyfill to
// `components/DataTable.tsx`'s reliance on `@tanstack/react-virtual` needing no jsdom shim of its
// own — S12-FE-01's `PhotoThumbnail` (`features/photo/components/PhotoOutboxList.tsx`) is the first
// component in this codebase to preview a locally-held `Blob`, so this is the first test run to need
// it. A fixed, non-unique stub URL is sufficient here: no test asserts on the URL's own value, only
// that an `<img src>` is present/absent.
if (typeof URL.createObjectURL !== 'function') {
  URL.createObjectURL = () => 'blob:mock-object-url'
}
if (typeof URL.revokeObjectURL !== 'function') {
  URL.revokeObjectURL = () => {}
}
