import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { SetEacDefaultButton } from './SetEacDefaultButton'
import * as api from '../api'
import type { SetEacVariantDefaultResult } from '../types'

vi.mock('../api', async () => {
  const actual = await vi.importActual<typeof import('../api')>('../api')
  return { ...actual, setEacVariantDefault: vi.fn() }
})

describe('SetEacDefaultButton', () => {
  beforeEach(() => {
    vi.mocked(api.setEacVariantDefault).mockReset()
  })

  it('DoD: is disabled and shows the current default when the local selection already equals it — transiently switching the dropdown must never look like a pending change', () => {
    render(
      <SetEacDefaultButton
        projectId="project-1"
        selectedVariant="CpiBased"
        persistedDefault="CpiBased"
        onSaved={vi.fn()}
      />,
    )
    const button = screen.getByRole('button')
    expect(button).toBeDisabled()
    expect(button).toHaveTextContent('ค่าเริ่มต้นของโครงการ: CPI-Based')
  })

  it('is enabled with the call-to-action label when the local selection differs from the persisted default', () => {
    render(
      <SetEacDefaultButton
        projectId="project-1"
        selectedVariant="CpiSpiBased"
        persistedDefault="CpiBased"
        onSaved={vi.fn()}
      />,
    )
    const button = screen.getByRole('button')
    expect(button).not.toBeDisabled()
    expect(button).toHaveTextContent('ตั้งเป็นค่าเริ่มต้นของโครงการ')
  })

  it('DoD: clicking persists exactly the currently-selected variant, and calls onSaved with the server-confirmed value', async () => {
    const user = userEvent.setup()
    const resultDto: SetEacVariantDefaultResult = { projectId: 'project-1', eacVariantDefault: 'CpiSpiBased' }
    vi.mocked(api.setEacVariantDefault).mockResolvedValueOnce(resultDto)
    const onSaved = vi.fn()

    render(
      <SetEacDefaultButton
        projectId="project-1"
        selectedVariant="CpiSpiBased"
        persistedDefault="CpiBased"
        onSaved={onSaved}
      />,
    )

    await user.click(screen.getByRole('button'))

    await waitFor(() => expect(onSaved).toHaveBeenCalledWith('CpiSpiBased'))
    expect(api.setEacVariantDefault).toHaveBeenCalledWith('project-1', 'CpiSpiBased')
  })

  it('shows the Thai error message inline and never calls onSaved on failure', async () => {
    const user = userEvent.setup()
    vi.mocked(api.setEacVariantDefault).mockRejectedValueOnce(
      new api.EvmApiError('คุณไม่มีสิทธิ์ตั้งค่าเริ่มต้นของ EAC สำหรับโครงการนี้', 403),
    )
    const onSaved = vi.fn()

    render(
      <SetEacDefaultButton
        projectId="project-1"
        selectedVariant="Atypical"
        persistedDefault="CpiBased"
        onSaved={onSaved}
      />,
    )

    await user.click(screen.getByRole('button'))

    await waitFor(() =>
      expect(screen.getByRole('alert')).toHaveTextContent('คุณไม่มีสิทธิ์ตั้งค่าเริ่มต้นของ EAC สำหรับโครงการนี้'),
    )
    expect(onSaved).not.toHaveBeenCalled()
  })
})
