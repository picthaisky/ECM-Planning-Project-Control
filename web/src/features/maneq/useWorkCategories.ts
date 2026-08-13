import { useEffect, useState } from 'react'
import { listWorkCategories } from './api'
import type { WorkCategoryDto } from './types'

/**
 * Loads the project's work-category catalogue (`GET /projects/{id}/work-categories`) for the
 * record/correction form's dropdown. Degrades to an empty list on any failure — the form then falls
 * back to a raw-GUID text input, so a catalogue outage never blocks logging.
 */
export function useWorkCategories(projectId: string): WorkCategoryDto[] {
  const [categories, setCategories] = useState<WorkCategoryDto[]>([])

  useEffect(() => {
    if (!projectId) return
    let cancelled = false
    listWorkCategories(projectId)
      .then((cats) => {
        if (!cancelled) setCategories(cats)
      })
      .catch(() => {
        if (!cancelled) setCategories([])
      })
    return () => {
      cancelled = true
    }
  }, [projectId])

  return categories
}
