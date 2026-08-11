import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { EvmPage } from './EvmPage'
import * as api from './api'
import { useAuthStore } from '../../store/authStore'
import type { EacVariantResponseDto, EvmResponseDto, EvmSnapshotDto, SetEacVariantDefaultResult } from './types'

vi.mock('./api', async () => {
  const actual = await vi.importActual<typeof import('./api')>('./api')
  return {
    ...actual,
    getEvm: vi.fn(),
    listEvmSnapshots: vi.fn(),
    setEacVariantDefault: vi.fn(),
  }
})

function variant(overrides: Partial<EacVariantResponseDto>): EacVariantResponseDto {
  return {
    variant: 'CpiBased',
    performanceFactor: null,
    etc: null,
    eac: null,
    vac: null,
    computable: false,
    reason: null,
    ...overrides,
  }
}

// evm-formulas.md Fixture A.
const fixtureA: EvmResponseDto = {
  projectId: 'project-1',
  dataDate: '2026-06-30T00:00:00+07:00',
  bac: '1000000.00',
  pv: '400000.00',
  ev: '300000.00',
  ac: '350000.00',
  sv: '-100000.00',
  cv: '-50000.00',
  spi: '0.750000',
  cpi: '0.857143',
  tcpiBac: '1.076923',
  tcpiEac: '0.857143',
  selectedVariant: 'CpiBased',
  variants: [
    variant({ variant: 'CpiBased', performanceFactor: '1.166667', etc: '816666.67', eac: '1166666.67', vac: '-166666.67', computable: true }),
    variant({ variant: 'Atypical', performanceFactor: '1.000000', etc: '700000.00', eac: '1050000.00', vac: '-50000.00', computable: true }),
    variant({ variant: 'CpiSpiBased', performanceFactor: '1.555556', etc: '1088888.89', eac: '1438888.89', vac: '-438888.89', computable: true }),
    variant({ variant: 'BottomUpEtc', computable: false, reason: 'ManualEtcNotSet' }),
    variant({ variant: 'CustomPf', computable: false, reason: 'CustomPfNotSet' }),
  ],
  warnings: [],
}

const sampleSnapshot: EvmSnapshotDto = {
  snapshotId: 'snap-1',
  projectId: 'project-1',
  dataDate: '2026-04-30T00:00:00+07:00',
  bac: '1000000.00',
  pv: '200000.00',
  ev: '180000.00',
  ac: '190000.00',
  eacVariant: 'CpiBased',
  performanceFactor: '1.10',
  eac: '1100000.00',
  etc: '910000.00',
  vac: '-100000.00',
  createdAt: '2026-05-01T09:00:00+07:00',
}

function renderPage(role: 'PM' | 'QS' | 'Executive' | 'Site' | 'Planning' | 'Admin' = 'PM') {
  useAuthStore.getState().login({
    accessToken: 'jwt',
    expiresAt: '2027-01-01T00:00:00+07:00',
    userId: 'user-1',
    tenantId: 'tenant-1',
    role,
  })

  return render(
    <MemoryRouter initialEntries={['/app/project-1/evm']}>
      <Routes>
        <Route path="/app/:projectId/evm" element={<EvmPage />} />
      </Routes>
    </MemoryRouter>,
  )
}

describe('EvmPage (S7-FE-01..04 integration)', () => {
  beforeEach(() => {
    vi.mocked(api.getEvm).mockReset()
    vi.mocked(api.listEvmSnapshots).mockReset()
    vi.mocked(api.setEacVariantDefault).mockReset()
    useAuthStore.getState().logout()
  })

  it('shows a loading state, then the real EVM screen (12-tile grid, 3-variant selector, chart) once data resolves', async () => {
    vi.mocked(api.getEvm).mockResolvedValueOnce(fixtureA)
    vi.mocked(api.listEvmSnapshots).mockResolvedValueOnce([])

    renderPage('PM')
    expect(screen.getByText('กำลังโหลดข้อมูล EVM...')).toBeInTheDocument()

    await waitFor(() => expect(screen.getByTestId('evm-metrics-grid')).toBeInTheDocument())
    expect(screen.getByTestId('evm-metrics-grid').children).toHaveLength(12)
    // S14-FE-02: the selector now shows all 5 engine variants, not only the 3 index-based ones.
    expect(screen.getAllByRole('radio')).toHaveLength(5)
    expect(screen.getByRole('img')).toBeInTheDocument() // SCurveChart's svg
  })

  it('shows a Thai error state on load failure, never a blank/broken screen', async () => {
    vi.mocked(api.getEvm).mockRejectedValueOnce(new api.EvmApiError('ไม่พบโครงการที่ระบุ', 404))
    vi.mocked(api.listEvmSnapshots).mockResolvedValueOnce([])

    renderPage('PM')

    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent('ไม่พบโครงการที่ระบุ'))
  })

  it('DoD (S7-FE-02): switching the EAC variant updates the tiles and forecast line together with NO additional API round trip', async () => {
    vi.mocked(api.getEvm).mockResolvedValueOnce(fixtureA)
    vi.mocked(api.listEvmSnapshots).mockResolvedValueOnce([])
    const user = userEvent.setup()

    renderPage('PM')
    await waitFor(() => expect(screen.getByTestId('evm-metrics-grid')).toBeInTheDocument())

    expect(api.getEvm).toHaveBeenCalledTimes(1)
    expect(screen.getByText('1,166,666.67 บาท')).toBeInTheDocument() // CpiBased EAC (default selected)
    expect(screen.getByRole('img').getAttribute('aria-label')).toContain('1,166,666.67')

    await user.click(screen.getByRole('radio', { name: /CPI × SPI/ }))

    // Tiles update instantly to the CpiSpiBased numbers…
    expect(screen.queryByText('1,166,666.67 บาท')).not.toBeInTheDocument()
    expect(screen.getByText('1,438,888.89 บาท')).toBeInTheDocument() // EAC
    expect(screen.getByText('1,088,888.89 บาท')).toBeInTheDocument() // ETC
    expect(screen.getByText('-438,888.89 บาท')).toBeInTheDocument() // VAC
    // …and the S-Curve forecast line follows it too, from the same already-fetched response.
    expect(screen.getByRole('img').getAttribute('aria-label')).toContain('1,438,888.89')

    // …with no refetch at all — this is the whole "no round trip" DoD.
    expect(api.getEvm).toHaveBeenCalledTimes(1)
  })

  it('DoD (S7-FE-03): PM sees the "set as default" button; Site does not (mirrors the server-side PM/QS/Executive-only role gate)', async () => {
    vi.mocked(api.getEvm).mockResolvedValue(fixtureA)
    vi.mocked(api.listEvmSnapshots).mockResolvedValue([])

    const { unmount } = renderPage('PM')
    await waitFor(() => expect(screen.getByTestId('evm-metrics-grid')).toBeInTheDocument())
    expect(screen.getByRole('button', { name: /ตั้งเป็นค่าเริ่มต้นของโครงการ|ค่าเริ่มต้นของโครงการ/ })).toBeInTheDocument()
    unmount()

    renderPage('Site')
    await waitFor(() => expect(screen.getByTestId('evm-metrics-grid')).toBeInTheDocument())
    expect(
      screen.queryByRole('button', { name: /ตั้งเป็นค่าเริ่มต้นของโครงการ|ค่าเริ่มต้นของโครงการ/ }),
    ).not.toBeInTheDocument()
  })

  it('DoD (S7-FE-03): switching the dropdown alone never persists — only the explicit button click does', async () => {
    vi.mocked(api.getEvm).mockResolvedValueOnce(fixtureA)
    vi.mocked(api.listEvmSnapshots).mockResolvedValueOnce([])
    const user = userEvent.setup()

    renderPage('PM')
    await waitFor(() => expect(screen.getByTestId('evm-metrics-grid')).toBeInTheDocument())

    await user.click(screen.getByRole('radio', { name: /Atypical/ }))
    expect(api.setEacVariantDefault).not.toHaveBeenCalled()

    // The button is enabled now (local selection Atypical != persisted default CpiBased)…
    const button = screen.getByRole('button', { name: 'ตั้งเป็นค่าเริ่มต้นของโครงการ' })
    expect(button).not.toBeDisabled()

    // …and only clicking it calls the write endpoint, with exactly the locally-selected variant.
    const resultDto: SetEacVariantDefaultResult = { projectId: 'project-1', eacVariantDefault: 'Atypical' }
    vi.mocked(api.setEacVariantDefault).mockResolvedValueOnce(resultDto)
    await user.click(button)

    await waitFor(() => expect(api.setEacVariantDefault).toHaveBeenCalledWith('project-1', 'Atypical'))
    // Persisting the default is still not a GET round trip.
    expect(api.getEvm).toHaveBeenCalledTimes(1)
  })

  it('ADR-0009: loads closed-period snapshots for the S-Curve independently of the live EVM read', async () => {
    vi.mocked(api.getEvm).mockResolvedValueOnce(fixtureA)
    vi.mocked(api.listEvmSnapshots).mockResolvedValueOnce([sampleSnapshot])

    renderPage('PM')
    await waitFor(() => expect(screen.getByRole('img')).toBeInTheDocument())

    // The actual historical-point-inclusion rule (strictly-before-data-date, sorted, merged with
    // the live point) is unit-tested directly against `buildSCurvePoints`
    // (`evmSelectors.test.ts`) — this only proves the wiring: the real project id reaches the real
    // snapshots endpoint, and a resolved snapshot doesn't break the page.
    expect(api.listEvmSnapshots).toHaveBeenCalledWith('project-1')
    expect(screen.getByTestId('evm-metrics-grid')).toBeInTheDocument()
  })

  it('DoD: today\'s real AC=0 gap (no cost-recording entity yet) renders honest reason text, in both the reason banner and the chart caption, never a fabricated number', async () => {
    const noActualCost: EvmResponseDto = {
      ...fixtureA,
      ac: '0.00',
      cpi: null,
      tcpiEac: null,
      variants: [
        variant({ variant: 'CpiBased', computable: false, reason: 'NoActualCost' }),
        variant({ variant: 'Atypical', computable: false, reason: 'NoActualCost' }),
        variant({ variant: 'CpiSpiBased', computable: false, reason: 'NoActualCost' }),
      ],
    }
    vi.mocked(api.getEvm).mockResolvedValueOnce(noActualCost)
    vi.mocked(api.listEvmSnapshots).mockResolvedValueOnce([])

    renderPage('PM')

    await waitFor(() =>
      expect(screen.getByText(/ตัวแปรที่เลือกยังคำนวณไม่ได้/)).toBeInTheDocument(),
    )
    // The same honest reason appears twice by design: once in the page-level reason banner, and
    // once in `SCurveChart`'s own caption explaining why no forecast line is drawn — both read the
    // same backend-provided `reason` code, never a frontend-guessed explanation.
    expect(screen.getAllByText(/ยังไม่มีข้อมูลค่าใช้จ่ายจริง/)).toHaveLength(2)
  })

  it('S14-FE-02 DoD: once BottomUpEtc/CustomPf are configured, selecting them shows fixtures A4/A5 exactly, with no extra round trip', async () => {
    // evm-formulas.md — worked examples A4 (BottomUpEtc, ETC_manual=760,000.00 -> EAC 1,110,000.00)
    // and A5 (CustomPf, PF_c=1.20 -> EAC 1,190,000.00), same Fixture A core inputs as `fixtureA`.
    const bothConfigured: EvmResponseDto = {
      ...fixtureA,
      variants: [
        ...fixtureA.variants.filter((v) => v.variant !== 'BottomUpEtc' && v.variant !== 'CustomPf'),
        variant({
          variant: 'BottomUpEtc',
          etc: '760000.00',
          eac: '1110000.00',
          vac: '-110000.00',
          computable: true,
        }),
        variant({
          variant: 'CustomPf',
          performanceFactor: '1.200000',
          etc: '840000.00',
          eac: '1190000.00',
          vac: '-190000.00',
          computable: true,
        }),
      ],
    }
    vi.mocked(api.getEvm).mockResolvedValueOnce(bothConfigured)
    vi.mocked(api.listEvmSnapshots).mockResolvedValueOnce([])
    const user = userEvent.setup()

    renderPage('PM')
    await waitFor(() => expect(screen.getByTestId('evm-metrics-grid')).toBeInTheDocument())

    // Neither option is disabled now that the inputs exist.
    expect(screen.getByRole('radio', { name: /Bottom-Up ETC/ })).not.toBeDisabled()
    expect(screen.getByRole('radio', { name: /Custom PF/ })).not.toBeDisabled()

    await user.click(screen.getByRole('radio', { name: /Bottom-Up ETC/ }))
    expect(screen.getByText('1,110,000.00 บาท')).toBeInTheDocument() // A4 EAC
    expect(screen.getByText('760,000.00 บาท')).toBeInTheDocument() // A4 ETC

    await user.click(screen.getByRole('radio', { name: /Custom PF/ }))
    expect(screen.getByText('1,190,000.00 บาท')).toBeInTheDocument() // A5 EAC
    expect(screen.getByText('840,000.00 บาท')).toBeInTheDocument() // A5 ETC

    expect(api.getEvm).toHaveBeenCalledTimes(1) // still zero extra round trips
  })

  it('S14-FE-02 DoD: BottomUpEtc/CustomPf are disabled with a Thai explanation, linking to Project Info, when their input is unset', async () => {
    vi.mocked(api.getEvm).mockResolvedValueOnce(fixtureA) // fixtureA's BottomUpEtc/CustomPf are unset
    vi.mocked(api.listEvmSnapshots).mockResolvedValueOnce([])

    renderPage('PM')
    await waitFor(() => expect(screen.getByTestId('evm-metrics-grid')).toBeInTheDocument())

    expect(screen.getByRole('radio', { name: /Bottom-Up ETC/ })).toBeDisabled()
    expect(screen.getByRole('radio', { name: /Custom PF/ })).toBeDisabled()
    expect(screen.getByText(/ยังไม่ได้กรอกประมาณการงานที่เหลือ/)).toBeInTheDocument()
    expect(screen.getByText(/ยังไม่ได้กำหนดตัวคูณผลการดำเนินงานเอง/)).toBeInTheDocument()

    const links = screen.getAllByRole('link', { name: 'ไปกรอกที่หน้าข้อมูลโครงการ →' })
    expect(links).toHaveLength(2)
    for (const link of links) {
      expect(link).toHaveAttribute('href', '/app/project-1/info')
    }
  })

  it('connects the ManualEtcPredatesBacChange warning to a real link back to Project Info (domain-rules.md §5.7)', async () => {
    const stale: EvmResponseDto = { ...fixtureA, warnings: ['ManualEtcPredatesBacChange'] }
    vi.mocked(api.getEvm).mockResolvedValueOnce(stale)
    vi.mocked(api.listEvmSnapshots).mockResolvedValueOnce([])

    renderPage('PM')

    await waitFor(() =>
      expect(screen.getByText(/มี Variation Order ที่อนุมัติแล้วเปลี่ยนงบประมาณ/)).toBeInTheDocument(),
    )
    const link = screen.getByRole('link', { name: 'ไปกรอกค่าประมาณการใหม่ที่หน้าข้อมูลโครงการ →' })
    expect(link).toHaveAttribute('href', '/app/project-1/info')
  })
})
