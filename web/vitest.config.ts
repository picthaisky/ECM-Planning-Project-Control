import { configDefaults, defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/setupTests.ts'],
    css: true,
    // `web/e2e/**` (S6-QA-01/02) holds real Playwright specs (`test.describe`/`test` from
    // `@playwright/test`, not vitest's own globals) — vitest has no default exclusion for a
    // project-local `e2e/` folder, so without this it picks the files up as if they were vitest
    // specs and fails immediately ("Playwright Test did not expect test.describe() to be called
    // here"), turning `npm test` red the moment Playwright specs exist on disk. Found and fixed
    // in this same session while setting up Playwright — keep `configDefaults.exclude` so the
    // usual node_modules/dist/etc. exclusions are not silently dropped by overriding `exclude`.
    exclude: [...configDefaults.exclude, 'e2e/**'],
  },
})
