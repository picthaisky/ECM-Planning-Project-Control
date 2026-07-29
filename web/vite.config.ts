import { defineConfig, loadEnv } from 'vite'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
export default defineConfig(({ mode }) => {
  // Deliberately NOT VITE_-prefixed: this only configures the Vite *dev server's* proxy target
  // (never bundled into client code — see docs/security/secrets-policy.md's VITE_* rule) and
  // only matters for `npm run dev` against a local API (docker compose or `dotnet run`).
  const env = loadEnv(mode, process.cwd(), '')
  const apiProxyTarget = env.API_PROXY_TARGET || 'http://localhost:5000'

  return {
    plugins: [react()],
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
