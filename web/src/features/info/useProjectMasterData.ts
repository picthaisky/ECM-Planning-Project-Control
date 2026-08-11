import { useCallback, useEffect, useState } from 'react'
import { getProject, ProjectApiError, updateProject } from './api'
import type { ProjectDetail, ProjectEacConfig, UpdateProjectPayload } from './types'

export type ProjectLoadState = 'loading' | 'ready' | 'error'
export type ProjectSaveState = 'idle' | 'saving' | 'error'

/**
 * Drives the S4-FE-02 Project Info master-data card: load-on-mount (`getProject`), edit-mode
 * toggle, and save (`updateProject`) — hand-rolled state (`useState`/`useCallback`), matching
 * `useImportWizard.ts`'s established pattern rather than introducing React Query for the first
 * time this sprint (no other feature in this codebase uses it yet; see this sprint's frontend
 * report for the note to reconcile against the `/cmplus-ui` skill's stated architecture).
 *
 * S14-FE-02: `project` is now the full `ProjectDetail` (`Project` + `ProjectEacConfig`) — see
 * `types.ts#ProjectDetail`'s remarks. `save` (backed by `updateProject`, which only ever returns
 * the base `Project` shape — the EAC fields are a disjoint, separately-audited command) **merges**
 * the response into the existing `ProjectDetail` rather than replacing it outright, so editing an
 * unrelated field (e.g. the project name) can never blank the EAC config fields on screen.
 * `updateEacConfig` is the matching merge setter for `EacAdvancedInputsCard`'s own successful save
 * (`setEacAdvancedInputs`), kept here (not duplicated as a second independent load) so both cards
 * on `ProjectInfoPage` share one `ProjectDetail` read.
 */
export function useProjectMasterData(projectId: string) {
  const [project, setProject] = useState<ProjectDetail | null>(null)
  const [loadState, setLoadState] = useState<ProjectLoadState>('loading')
  const [loadError, setLoadError] = useState<string | null>(null)
  const [isEditing, setIsEditing] = useState(false)
  const [saveState, setSaveState] = useState<ProjectSaveState>('idle')
  const [saveError, setSaveError] = useState<string | null>(null)

  const load = useCallback(async () => {
    setLoadState('loading')
    setLoadError(null)
    try {
      const data = await getProject(projectId)
      setProject(data)
      setLoadState('ready')
    } catch (error) {
      setLoadState('error')
      setLoadError(error instanceof ProjectApiError ? error.message : 'โหลดข้อมูลโครงการไม่สำเร็จ')
    }
  }, [projectId])

  useEffect(() => {
    void load()
  }, [load])

  const startEdit = useCallback(() => {
    setSaveError(null)
    setIsEditing(true)
  }, [])

  const cancelEdit = useCallback(() => {
    setSaveError(null)
    setIsEditing(false)
  }, [])

  const save = useCallback(
    async (payload: UpdateProjectPayload) => {
      setSaveState('saving')
      setSaveError(null)
      try {
        const updated = await updateProject(projectId, payload)
        // Merge, never replace — `updated` (the real `ProjectDto` shape) carries no EAC fields at
        // all; a bare `setProject(updated)` would silently blank `eacManualEtc`/
        // `eacCustomPerformanceFactor`/etc. off screen even though nothing server-side changed them.
        setProject((prev) => (prev ? { ...prev, ...updated } : prev))
        setSaveState('idle')
        setIsEditing(false)
        return true
      } catch (error) {
        setSaveState('error')
        setSaveError(error instanceof ProjectApiError ? error.message : 'บันทึกข้อมูลโครงการไม่สำเร็จ')
        return false
      }
    },
    [projectId],
  )

  /** Merges a fresh `ProjectEacConfig` (from a successful `setEacAdvancedInputs`/
   * `setEacVariantDefault` call elsewhere) into the loaded project — the mirror image of `save`'s
   * own merge, in the other direction. No-ops if `project` has not loaded yet (nothing to merge
   * into); the caller's own save flow does not depend on this succeeding. */
  const updateEacConfig = useCallback((patch: Partial<ProjectEacConfig>) => {
    setProject((prev) => (prev ? { ...prev, ...patch } : prev))
  }, [])

  return {
    project,
    loadState,
    loadError,
    reload: load,
    isEditing,
    startEdit,
    cancelEdit,
    saveState,
    saveError,
    save,
    updateEacConfig,
  }
}
