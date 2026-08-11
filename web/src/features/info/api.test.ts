import { AxiosError, AxiosHeaders } from 'axios'
import type { InternalAxiosRequestConfig } from 'axios'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import {
  downloadProgressTemplate,
  getImportJob,
  getImportJobHistory,
  getProject,
  importFile,
  ImportApiError,
  ProjectApiError,
  setEacAdvancedInputs,
  updateProject,
} from './api'
import { apiClient } from '../../services/apiClient'
import type { FileImportJob, Project, ProjectDetail, UpdateProjectPayload } from './types'

vi.mock('../../services/apiClient', () => ({
  apiClient: { get: vi.fn(), post: vi.fn(), put: vi.fn() },
}))

const sampleJob: FileImportJob = {
  id: 'job-1',
  projectId: 'project-1',
  fileName: 'schedule.xer',
  format: 'Xer',
  status: 'Succeeded',
  rowsImported: 128,
  errorJson: null,
  startedAt: '2026-07-27T09:00:00+07:00',
  finishedAt: '2026-07-27T09:00:05+07:00',
  createdByUserId: 'user-1',
}

function makeConfig(url: string): InternalAxiosRequestConfig {
  return { url, headers: new AxiosHeaders() } as InternalAxiosRequestConfig
}

function makeError(status: number, data: unknown): AxiosError {
  const config = makeConfig('/x')
  return new AxiosError('Request failed', String(status), config, undefined, {
    status,
    statusText: '',
    data,
    headers: {},
    config,
  })
}

describe('features/info/api', () => {
  beforeEach(() => {
    vi.mocked(apiClient.post).mockReset()
    vi.mocked(apiClient.get).mockReset()
    vi.mocked(apiClient.put).mockReset()
  })

  describe('importFile', () => {
    it('posts multipart form data with the file under the "file" field to the routed endpoint', async () => {
      vi.mocked(apiClient.post).mockResolvedValueOnce({ data: sampleJob })
      const file = new File(['PMXER...'], 'schedule.xer')

      const result = await importFile('project-1', file, 'xer')

      expect(apiClient.post).toHaveBeenCalledWith(
        '/projects/project-1/import/xer',
        expect.any(FormData),
      )
      const formData = vi.mocked(apiClient.post).mock.calls[0][1] as FormData
      expect(formData.get('file')).toBe(file)
      expect(result).toEqual(sampleJob)
    })

    it('translates a request-level ProblemDetails error to a Thai ImportApiError', async () => {
      vi.mocked(apiClient.post).mockRejectedValueOnce(
        makeError(404, { detail: 'ImportProjectNotFound', title: 'not found' }),
      )
      const file = new File(['x'], 'schedule.xer')

      await expect(importFile('project-1', file, 'xer')).rejects.toMatchObject({
        name: 'ImportApiError',
        message: 'ไม่พบโครงการที่ระบุ',
        status: 404,
      })
    })

    it('wraps a non-Axios error in a generic Thai ImportApiError', async () => {
      vi.mocked(apiClient.post).mockRejectedValueOnce(new Error('network down'))
      const file = new File(['x'], 'schedule.xer')

      await expect(importFile('project-1', file, 'xer')).rejects.toBeInstanceOf(ImportApiError)
      await expect(importFile('project-1', file, 'xer')).rejects.toMatchObject({
        message: 'ดำเนินการไม่สำเร็จ กรุณาลองใหม่อีกครั้ง',
      })
    })
  })

  describe('getImportJob', () => {
    it('fetches a single job by id', async () => {
      vi.mocked(apiClient.get).mockResolvedValueOnce({ data: sampleJob })

      const result = await getImportJob('project-1', 'job-1')

      expect(apiClient.get).toHaveBeenCalledWith('/projects/project-1/import/jobs/job-1')
      expect(result).toEqual(sampleJob)
    })

    it('translates ImportJobNotFound', async () => {
      vi.mocked(apiClient.get).mockRejectedValueOnce(
        makeError(404, { detail: 'ImportJobNotFound' }),
      )

      await expect(getImportJob('project-1', 'missing')).rejects.toMatchObject({
        message: 'ไม่พบประวัติการนำเข้านี้',
      })
    })
  })

  describe('getImportJobHistory', () => {
    it('requests newest-first history with default paging', async () => {
      vi.mocked(apiClient.get).mockResolvedValueOnce({ data: [sampleJob] })

      const result = await getImportJobHistory('project-1')

      expect(apiClient.get).toHaveBeenCalledWith('/projects/project-1/import/jobs', {
        params: { page: 1, pageSize: 20 },
      })
      expect(result).toEqual([sampleJob])
    })

    it('resolves to an empty list rather than throwing when a project has no history yet', async () => {
      vi.mocked(apiClient.get).mockResolvedValueOnce({ data: [] })

      await expect(getImportJobHistory('project-1')).resolves.toEqual([])
    })
  })

  describe('downloadProgressTemplate', () => {
    it('downloads the blob and triggers a browser save via an anchor click', async () => {
      const blob = new Blob(['xlsx-bytes'])
      vi.mocked(apiClient.get).mockResolvedValueOnce({ data: blob })

      const createObjectURL = vi.fn(() => 'blob:mock-url')
      const revokeObjectURL = vi.fn()
      vi.stubGlobal('URL', { ...URL, createObjectURL, revokeObjectURL })

      const clickSpy = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {})

      await downloadProgressTemplate('project-1')

      expect(apiClient.get).toHaveBeenCalledWith('/projects/project-1/import/xlsx/template', {
        responseType: 'blob',
      })
      expect(createObjectURL).toHaveBeenCalledWith(blob)
      expect(clickSpy).toHaveBeenCalledTimes(1)
      expect(revokeObjectURL).toHaveBeenCalledWith('blob:mock-url')

      clickSpy.mockRestore()
      vi.unstubAllGlobals()
    })

    it('throws a Thai ImportApiError when the template download fails', async () => {
      vi.mocked(apiClient.get).mockRejectedValueOnce(makeError(500, {}))

      await expect(downloadProgressTemplate('project-1')).rejects.toMatchObject({
        name: 'ImportApiError',
      })
    })
  })

  const sampleProject: Project = {
    id: 'project-1',
    name: 'Riverside Condominium Tower B',
    code: 'RCT-B',
    owner: 'Siam Riverside Development PLC',
    contractStart: '2025-10-01T00:00:00+07:00',
    contractFinish: '2027-03-31T00:00:00+07:00',
    bac: '850000000.00',
    contractValue: '850000000.00',
    retentionRate: '5.00',
    advanceRate: '10.00',
    retentionCapPercentage: null,
    retentionRelease1Percentage: '50.00',
    defectsLiabilityMonths: 24,
    advanceAmountPaid: null,
    advanceRecoveryMethod: 'ProRata',
    advanceRecoveryStartPct: null,
    advanceRecoveryRatePct: null,
    advanceRecoveryEndPct: null,
  }

  const sampleProjectDetail: ProjectDetail = {
    ...sampleProject,
    eacVariantDefault: 'CpiBased',
    eacManualEtc: null,
    eacCustomPerformanceFactor: null,
    eacManualEtcStaleSince: null,
  }

  describe('getProject', () => {
    it('fetches the project (S4-FE-02 view half) including its S14 EAC config', async () => {
      vi.mocked(apiClient.get).mockResolvedValueOnce({ data: sampleProjectDetail })

      const result = await getProject('project-1')

      expect(apiClient.get).toHaveBeenCalledWith('/projects/project-1')
      expect(result).toEqual(sampleProjectDetail)
    })

    it('translates a 404 ProjectNotFound to a Thai ProjectApiError', async () => {
      vi.mocked(apiClient.get).mockRejectedValueOnce(
        makeError(404, { detail: 'ProjectNotFound', type: 'https://cmplus.dev/problems/not-found' }),
      )

      await expect(getProject('missing')).rejects.toMatchObject({
        name: 'ProjectApiError',
        message: 'ไม่พบโครงการที่ระบุ',
        status: 404,
      })
    })
  })

  describe('updateProject', () => {
    const payload: UpdateProjectPayload = {
      name: sampleProject.name,
      code: sampleProject.code,
      owner: sampleProject.owner,
      contractStart: sampleProject.contractStart,
      contractFinish: sampleProject.contractFinish,
      bac: sampleProject.bac,
      contractValue: sampleProject.contractValue,
      retentionRate: sampleProject.retentionRate,
      advanceRate: sampleProject.advanceRate,
      retentionCapPercentage: sampleProject.retentionCapPercentage,
      retentionRelease1Percentage: sampleProject.retentionRelease1Percentage,
      defectsLiabilityMonths: sampleProject.defectsLiabilityMonths,
      advanceAmountPaid: sampleProject.advanceAmountPaid,
      advanceRecoveryMethod: sampleProject.advanceRecoveryMethod,
      advanceRecoveryStartPct: sampleProject.advanceRecoveryStartPct,
      advanceRecoveryRatePct: sampleProject.advanceRecoveryRatePct,
      advanceRecoveryEndPct: sampleProject.advanceRecoveryEndPct,
    }

    it('PUTs the full field set and returns the updated project', async () => {
      vi.mocked(apiClient.put).mockResolvedValueOnce({ data: sampleProject })

      const result = await updateProject('project-1', payload)

      expect(apiClient.put).toHaveBeenCalledWith('/projects/project-1', payload)
      expect(result).toEqual(sampleProject)
    })

    it('translates a 400 validation-error problem type to a Thai ProjectApiError', async () => {
      vi.mocked(apiClient.put).mockRejectedValueOnce(
        makeError(400, {
          type: 'https://cmplus.dev/problems/validation-error',
          detail: 'ContractFinish cannot be earlier than ContractStart.',
        }),
      )

      await expect(updateProject('project-1', payload)).rejects.toMatchObject({
        name: 'ProjectApiError',
        message: 'ข้อมูลไม่ถูกต้อง กรุณาตรวจสอบค่าที่กรอกอีกครั้ง',
        status: 400,
      })
    })

    it('wraps a non-Axios error in a generic Thai ProjectApiError', async () => {
      vi.mocked(apiClient.put).mockRejectedValueOnce(new Error('network down'))

      await expect(updateProject('project-1', payload)).rejects.toBeInstanceOf(ProjectApiError)
    })

    // Verified directly against the real, live `PUT /api/v1/projects/{id}` during this sprint's
    // e2e check: `UpdateProjectCommandValidator` failures never produce `type="validation-error"`
    // on this Result<T>-shaped command — they fall into `ResultProblemMapper`'s "code not in the
    // table" bucket instead: `type=".../problems/bad-request"`, `detail=<raw English
    // FluentValidation text>` (e.g. "'Retention Rate' must be between 0 and 100. You entered
    // 150.00."). This must never reach the Thai-first UI verbatim.
    it('translates the real "bad-request" fallback bucket to a generic Thai message, never leaking the raw English FluentValidation text', async () => {
      vi.mocked(apiClient.put).mockRejectedValueOnce(
        makeError(400, {
          type: 'https://cmplus.dev/problems/bad-request',
          title: 'The request could not be completed.',
          detail: "'Retention Rate' must be between 0 and 100. You entered 150.00.",
        }),
      )

      await expect(updateProject('project-1', payload)).rejects.toMatchObject({
        name: 'ProjectApiError',
        message: 'ข้อมูลไม่ถูกต้อง กรุณาตรวจสอบค่าที่กรอกอีกครั้ง',
        status: 400,
      })
    })
  })

  describe('setEacAdvancedInputs (S14-BE-03)', () => {
    it('PUTs both fields (full-representation) and returns the fresh result', async () => {
      vi.mocked(apiClient.put).mockResolvedValueOnce({
        data: {
          projectId: 'project-1',
          eacManualEtc: '760000.00',
          eacCustomPerformanceFactor: '1.2000',
          eacManualEtcStaleSince: null,
        },
      })

      const result = await setEacAdvancedInputs('project-1', {
        eacManualEtc: '760000.00',
        eacCustomPerformanceFactor: '1.2000',
      })

      expect(apiClient.put).toHaveBeenCalledWith('/projects/project-1/eac-advanced-inputs', {
        eacManualEtc: '760000.00',
        eacCustomPerformanceFactor: '1.2000',
      })
      expect(result.eacManualEtc).toBe('760000.00')
      expect(result.eacManualEtcStaleSince).toBeNull()
    })

    it('translates the "cannot clear while active" guard (ProjectEacManualEtcRequiredForBottomUpEtc) to a Thai message pointing at the EVM screen', async () => {
      vi.mocked(apiClient.put).mockRejectedValueOnce(
        makeError(400, {
          type: 'https://cmplus.dev/problems/eac-manual-etc-required-for-bottom-up-etc',
          detail: 'ProjectEacManualEtcRequiredForBottomUpEtc',
        }),
      )

      await expect(
        setEacAdvancedInputs('project-1', { eacManualEtc: null, eacCustomPerformanceFactor: null }),
      ).rejects.toMatchObject({
        name: 'ProjectApiError',
        status: 400,
        message: expect.stringContaining('EVM S-Curve'),
      })
    })

    it('translates the CustomPf equivalent guard to its own Thai message', async () => {
      vi.mocked(apiClient.put).mockRejectedValueOnce(
        makeError(400, {
          type: 'https://cmplus.dev/problems/eac-custom-performance-factor-required-for-custom-pf',
          detail: 'ProjectEacCustomPerformanceFactorRequiredForCustomPf',
        }),
      )

      await expect(
        setEacAdvancedInputs('project-1', { eacManualEtc: null, eacCustomPerformanceFactor: null }),
      ).rejects.toMatchObject({
        name: 'ProjectApiError',
        message: expect.stringContaining('Custom PF'),
      })
    })

    it('maps a bodyless 403 to a specific Thai permission message, not the generic fallback', async () => {
      vi.mocked(apiClient.put).mockRejectedValueOnce(makeError(403, undefined))

      await expect(
        setEacAdvancedInputs('project-1', { eacManualEtc: '1.00', eacCustomPerformanceFactor: null }),
      ).rejects.toMatchObject({
        name: 'ProjectApiError',
        status: 403,
        message: 'คุณไม่มีสิทธิ์ตั้งค่า EAC ขั้นสูงของโครงการนี้',
      })
    })
  })
})
