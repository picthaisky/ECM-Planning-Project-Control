import { useCallback, useEffect, useState } from 'react'
import { getProject, ProjectApiError, updateProject } from './api'
import type { Project, UpdateProjectPayload } from './types'

export type ProjectLoadState = 'loading' | 'ready' | 'error'
export type ProjectSaveState = 'idle' | 'saving' | 'error'

/**
 * Drives the S4-FE-02 Project Info master-data card: load-on-mount (`getProject`), edit-mode
 * toggle, and save (`updateProject`) — hand-rolled state (`useState`/`useCallback`), matching
 * `useImportWizard.ts`'s established pattern rather than introducing React Query for the first
 * time this sprint (no other feature in this codebase uses it yet; see this sprint's frontend
 * report for the note to reconcile against the `/cmplus-ui` skill's stated architecture).
 */
export function useProjectMasterData(projectId: string) {
  const [project, setProject] = useState<Project | null>(null)
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
        setProject(updated)
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
  }
}
