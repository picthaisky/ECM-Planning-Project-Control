import { useCallback, useEffect, useRef, useState } from 'react'
import {
  downloadProgressTemplate,
  getImportJob,
  getImportJobHistory,
  importFile,
  ImportApiError,
} from './api'
import type { FileImportJob, ImportRouteFormat } from './types'

export type ImportUploadPhase = 'idle' | 'uploading' | 'polling' | 'done'

export interface ImportUploadState {
  phase: ImportUploadPhase
  routeFormat: ImportRouteFormat | null
  fileName: string | null
  job: FileImportJob | null
  /** Set only when the *request* itself failed before any job could be created (unknown project,
   * unsupported route segment, expired session, network error) — a rejected *file* is instead a
   * completed job whose `job.status === 'Failed'`; see `ImportApiError`'s remarks. */
  requestError: string | null
}

export type HistoryState = 'loading' | 'ready' | 'error'
export type TemplateDownloadState = 'idle' | 'loading' | 'error'

const POLL_INTERVAL_MS = 1500
// ~60s ceiling before giving up on polling and pointing the user at the history list instead —
// today's backend (S3-BE-04) resolves synchronously so this path isn't exercised yet, but the API
// contract is job-based and a future async worker must not poll forever.
const MAX_POLL_ATTEMPTS = 40

const initialUploadState: ImportUploadState = {
  phase: 'idle',
  routeFormat: null,
  fileName: null,
  job: null,
  requestError: null,
}

/**
 * Drives the S3-FE-01 import wizard's upload -> progress -> results/errors flow plus the import
 * history list, against the real `ImportController` endpoints (`./api.ts`). One instance is shared
 * by `ImportUploadCard` and `ImportHistoryCard` via `ImportWizard` so a completed upload refreshes
 * the history list automatically.
 */
export function useImportWizard(projectId: string) {
  const [upload, setUpload] = useState<ImportUploadState>(initialUploadState)
  const [history, setHistory] = useState<FileImportJob[]>([])
  const [historyState, setHistoryState] = useState<HistoryState>('loading')
  const [historyError, setHistoryError] = useState<string | null>(null)
  const [templateDownload, setTemplateDownload] = useState<{
    state: TemplateDownloadState
    message: string | null
  }>({ state: 'idle', message: null })

  // Incremented on every new upload/reset so a stale poll loop (from a superseded upload) can
  // detect it's no longer current and stop touching state.
  const pollTokenRef = useRef(0)

  const refreshHistory = useCallback(async () => {
    setHistoryState('loading')
    setHistoryError(null)
    try {
      const jobs = await getImportJobHistory(projectId)
      setHistory(jobs)
      setHistoryState('ready')
    } catch (error) {
      setHistoryState('error')
      setHistoryError(
        error instanceof ImportApiError ? error.message : 'โหลดประวัติการนำเข้าไม่สำเร็จ',
      )
    }
  }, [projectId])

  useEffect(() => {
    void refreshHistory()
  }, [refreshHistory])

  const pollJob = useCallback(
    async (jobId: string, token: number) => {
      for (let attempt = 0; attempt < MAX_POLL_ATTEMPTS; attempt += 1) {
        await new Promise((resolve) => setTimeout(resolve, POLL_INTERVAL_MS))
        if (pollTokenRef.current !== token) return // superseded by a newer upload/reset

        try {
          const job = await getImportJob(projectId, jobId)
          if (pollTokenRef.current !== token) return

          if (job.status !== 'Pending') {
            setUpload((prev) => ({ ...prev, phase: 'done', job }))
            void refreshHistory()
            return
          }

          setUpload((prev) => ({ ...prev, job }))
        } catch (error) {
          if (pollTokenRef.current !== token) return
          setUpload((prev) => ({
            ...prev,
            phase: 'done',
            requestError:
              error instanceof ImportApiError ? error.message : 'ตรวจสอบสถานะการนำเข้าไม่สำเร็จ',
          }))
          return
        }
      }

      if (pollTokenRef.current !== token) return
      setUpload((prev) => ({
        ...prev,
        phase: 'done',
        requestError:
          'การนำเข้าใช้เวลานานเกินไป กรุณาตรวจสอบสถานะภายหลังจากประวัติการนำเข้าด้านล่าง',
      }))
    },
    [projectId, refreshHistory],
  )

  const startImport = useCallback(
    async (file: File, routeFormat: ImportRouteFormat) => {
      const token = pollTokenRef.current + 1
      pollTokenRef.current = token

      setUpload({
        phase: 'uploading',
        routeFormat,
        fileName: file.name,
        job: null,
        requestError: null,
      })

      try {
        const job = await importFile(projectId, file, routeFormat)
        if (pollTokenRef.current !== token) return

        if (job.status === 'Pending') {
          setUpload((prev) => ({ ...prev, phase: 'polling', job }))
          void pollJob(job.id, token)
          return
        }

        setUpload((prev) => ({ ...prev, phase: 'done', job }))
        void refreshHistory()
      } catch (error) {
        if (pollTokenRef.current !== token) return
        setUpload((prev) => ({
          ...prev,
          phase: 'done',
          requestError:
            error instanceof ImportApiError
              ? error.message
              : 'นำเข้าไฟล์ไม่สำเร็จ กรุณาลองใหม่อีกครั้ง',
        }))
      }
    },
    [projectId, pollJob, refreshHistory],
  )

  const resetUpload = useCallback(() => {
    pollTokenRef.current += 1 // invalidate any in-flight poll loop
    setUpload(initialUploadState)
  }, [])

  /** For a purely client-side rejection (wrong extension for the row clicked) that never reaches
   * the network — shown through the same result-banner UI as a request/job failure so there is
   * only one error-display code path in `ImportUploadCard`. */
  const setClientValidationError = useCallback((message: string) => {
    pollTokenRef.current += 1
    setUpload({ ...initialUploadState, phase: 'done', requestError: message })
  }, [])

  const downloadTemplate = useCallback(async () => {
    setTemplateDownload({ state: 'loading', message: null })
    try {
      await downloadProgressTemplate(projectId)
      setTemplateDownload({ state: 'idle', message: null })
    } catch (error) {
      setTemplateDownload({
        state: 'error',
        message: error instanceof ImportApiError ? error.message : 'ดาวน์โหลดเทมเพลตไม่สำเร็จ',
      })
    }
  }, [projectId])

  return {
    upload,
    startImport,
    resetUpload,
    setClientValidationError,
    history,
    historyState,
    historyError,
    refreshHistory,
    templateDownload,
    downloadTemplate,
  }
}
