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
