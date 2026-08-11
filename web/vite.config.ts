import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { defineConfig, loadEnv, transformWithOxc } from 'vite'
import type { Plugin } from 'vite'
import react from '@vitejs/plugin-react'

/**
 * S13-FE-02: builds `web/src/sw.ts` into `dist/sw.js` as a **separate** artifact from the main app
 * bundle — deliberately not a new npm dependency (no `vite-plugin-pwa`/workbox): `sw.ts` is plain,
 * dependency-free browser code, so hand-compiling it with Vite's own bundled transformer
 * (`transformWithOxc`, public Vite API — this project's Vite 8 ships Rolldown/oxc, not esbuild;
 * `transformWithEsbuild` is deprecated here and errors unless `esbuild` is installed as a *separate*
 * dependency, which this repo deliberately does not add just for this) needs nothing beyond what is
 * already installed, and keeps the whole precache/versioning scheme in one reviewable file instead of
 * a generated one.
 *
 * Runs only in `generateBundle` (a build-only Rollup/Rolldown hook — never during `vite dev`/
 * `preview`), after the bundler has already decided the main build's own output file names, so the
 * precache list below is always this exact build's real, hashed asset names — never a guess. See
 * `src/sw.ts`'s own module comment for the full versioning/update-flow rationale this plugin sets up.
 */
function serviceWorkerPlugin(): Plugin {
  return {
    name: 'cmplus-service-worker',
    apply: 'build',
    async generateBundle(_outputOptions, bundle) {
      // CI (S13-DO-01) sets this to the deploy's immutable identifier (e.g. the git commit SHA)
      // before running `npm run build`; an ad hoc local build still gets a fresh, distinct version
      // every time so the update flow is manually testable without CI at all.
      const version = process.env.VITE_BUILD_ID?.trim() || `dev-${Date.now()}`

      const bundledAssetUrls = Object.values(bundle).map((chunkOrAsset) => `/${chunkOrAsset.fileName}`)
      // `/` and `/index.html` both precached: a fresh install's `install` event fetches whichever
      // path the *browser* actually serves that content at, and the offline navigation fallback
      // (`src/sw.ts#respondToNavigation`) tries `/index.html` first, `/` second — precaching both
      // means neither lookup can miss regardless of which one the host server prefers.
      // `/favicon.svg` is `public/`-copied (outside the bundler's own module graph), so it is named
      // here explicitly rather than discovered from `bundle`.
      const precacheUrls = Array.from(new Set(['/', '/index.html', '/favicon.svg', ...bundledAssetUrls]))

      const swEntryPath = fileURLToPath(new URL('./src/sw.ts', import.meta.url))
      const swSource = readFileSync(swEntryPath, 'utf-8')

      // `define` (oxc's own AST-level identifier substitution, not a naive text replace) is what
      // turns `src/sw.ts`'s `declare const __CM_SW_VERSION__`/`__CM_PRECACHE_URLS__` (type-only,
      // erased by this same transform) into this build's real values.
      const compiled = await transformWithOxc(swSource, swEntryPath, {
        lang: 'ts',
        target: 'es2022',
        define: {
          __CM_SW_VERSION__: JSON.stringify(version),
          __CM_PRECACHE_URLS__: JSON.stringify(precacheUrls),
        },
      })

      // Emitted at the output root (scope `/`, matching `navigator.serviceWorker.register('/sw.js')`
      // in `registerServiceWorker.ts`) so its default controlling scope covers the whole app.
      this.emitFile({ type: 'asset', fileName: 'sw.js', source: compiled.code })
    },
  }
}

// https://vite.dev/config/
export default defineConfig(({ mode }) => {
  // Deliberately NOT VITE_-prefixed: this only configures the Vite *dev server's* proxy target
  // (never bundled into client code — see docs/security/secrets-policy.md's VITE_* rule) and
  // only matters for `npm run dev` against a local API (docker compose or `dotnet run`).
  const env = loadEnv(mode, process.cwd(), '')
  const apiProxyTarget = env.API_PROXY_TARGET || 'http://localhost:5000'

  return {
    plugins: [react(), serviceWorkerPlugin()],
    server: {
      proxy: {
        // apiClient.ts's default baseURL is the relative '/api/v1', so the same build works
        // behind whatever same-origin reverse proxy fronts the API per environment; in dev, this
        // proxy plays that role — a same-origin, server-side hop to the real backend, so no CORS
        // policy is required on the API for local development.
        '/api': {
          target: apiProxyTarget,
          changeOrigin: true,
        },
      },
    },
  }
})
