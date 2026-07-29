import { create } from 'zustand'

const STORAGE_KEY = 'cmplus.currentProjectId'

/**
 * Interim "current project" seam (S4-FE-01).
 *
 * There is no `GET /api/v1/projects` (tenant project list) or `GET /api/v1/projects/{id}`
 * endpoint yet — Sprint 4's backend shipped exactly three endpoints (`wbs-tree`, `PUT
 * /projects/{id}`, `progress/batch`), all of which require a `projectId` the client must already
 * know. `LoginResult` (S2-BE-01) carries only `userId`/`tenantId`/`role`, never a project — so
 * there is genuinely no API-driven way to resolve "which project" after login yet.
 *
 * Until a real project-list/selector endpoint exists (flagged to `backend-developer`/
 * `system-architect` as follow-up work), `RequireProject` (`routes/RequireProject.tsx`) gates
 * every `/app/:projectId/*` route on this store; `SelectProjectPage` lets a user paste in a
 * project id once (e.g. from the seeded dev data or a colleague) and it is remembered here for
 * next time. A `projectId` is not a secret (unlike the JWT in `authStore.ts`, which is
 * deliberately never persisted) — persisting it to `localStorage` is a plain convenience so a
 * page refresh does not force re-entering it, and carries none of the token-exfiltration
 * tradeoffs `authStore.ts` documents.
 */
interface ProjectState {
  currentProjectId: string | null
  setCurrentProjectId: (projectId: string) => void
  clearCurrentProjectId: () => void
}

function readPersistedProjectId(): string | null {
  if (typeof window === 'undefined') return null
  try {
    return window.localStorage.getItem(STORAGE_KEY)
  } catch {
    // Storage can throw in locked-down/private-browsing contexts — degrade to "no project
    // remembered" rather than crashing the whole app shell over a convenience feature.
    return null
  }
}

function persistProjectId(projectId: string | null): void {
  if (typeof window === 'undefined') return
  try {
    if (projectId) {
      window.localStorage.setItem(STORAGE_KEY, projectId)
    } else {
      window.localStorage.removeItem(STORAGE_KEY)
    }
  } catch {
    // Best-effort only — see readPersistedProjectId.
  }
}

export const useProjectStore = create<ProjectState>((set) => ({
  currentProjectId: readPersistedProjectId(),
  setCurrentProjectId: (projectId) => {
    persistProjectId(projectId)
    set({ currentProjectId: projectId })
  },
  clearCurrentProjectId: () => {
    persistProjectId(null)
    set({ currentProjectId: null })
  },
}))
