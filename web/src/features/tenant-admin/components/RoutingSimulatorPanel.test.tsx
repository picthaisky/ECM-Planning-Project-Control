import { describe, expect, it, vi } from 'vitest'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { RoutingSimulatorPanel } from './RoutingSimulatorPanel'
import type { ApprovalRoutingSimulation } from '../types'

const VALID_PROJECT_ID = '3fa85f64-5717-4562-b3fc-2c963f66afa6'

const simulation: ApprovalRoutingSimulation = {
  documentType: 'VariationOrder',
  projectId: VALID_PROJECT_ID,
  inputAmount: '2400000.00',
  routingAmount: '2400000.00',
  approvalPolicyId: 'policy-2',
  approvalPolicyVersion: 2,
  usedFallbackChain: false,
  steps: [
    { stepNo: 1, requiredRole: 'PM', quorumCount: 1 },
    { stepNo: 2, requiredRole: 'ProjectDirector', quorumCount: 1 },
  ],
  escalationApplied: true,
  allowSelfApproval: false,
  multipleActivePoliciesDetected: false,
  ambiguousActivePolicies: [],
}

const defaultProps = {
  documentType: 'VariationOrder' as const,
  defaultProjectId: null,
  state: 'idle' as const,
  error: null,
  result: null,
  onSimulate: vi.fn(),
}

describe('RoutingSimulatorPanel', () => {
  it('rejects an invalid Project ID before calling onSimulate', async () => {
    const onSimulate = vi.fn()
    render(<RoutingSimulatorPanel {...defaultProps} onSimulate={onSimulate} />)

    await userEvent.type(screen.getByLabelText('รหัสโครงการ (Project ID)'), 'not-a-guid')
    await userEvent.type(screen.getByLabelText('จำนวนเงินสมมติ (บาท)'), '1000')
    await userEvent.click(screen.getByRole('button', { name: 'ทดลอง routing' }))

    expect(onSimulate).not.toHaveBeenCalled()
    expect(screen.getByRole('alert')).toHaveTextContent('รูปแบบ GUID')
  })

  it('rejects an empty/invalid amount before calling onSimulate', async () => {
    const onSimulate = vi.fn()
    render(<RoutingSimulatorPanel {...defaultProps} onSimulate={onSimulate} />)

    await userEvent.type(screen.getByLabelText('รหัสโครงการ (Project ID)'), VALID_PROJECT_ID)
    await userEvent.click(screen.getByRole('button', { name: 'ทดลอง routing' }))

    expect(onSimulate).not.toHaveBeenCalled()
    expect(screen.getByRole('alert')).toHaveTextContent('จำนวนเงินสมมติ')
  })

  it('calls onSimulate with the trimmed project id and amount when both are valid', async () => {
    const onSimulate = vi.fn()
    render(<RoutingSimulatorPanel {...defaultProps} onSimulate={onSimulate} />)

    await userEvent.type(screen.getByLabelText('รหัสโครงการ (Project ID)'), `  ${VALID_PROJECT_ID}  `)
    // A whole number, not "2400000.00" — jsdom's <input type="number"> sanitizes away a
    // "".00" fractional part typed keystroke-by-keystroke, which is a test-harness quirk unrelated
    // to this component (it never reformats the value itself; it sends whatever the input holds).
    await userEvent.type(screen.getByLabelText('จำนวนเงินสมมติ (บาท)'), '2400000')
    await userEvent.click(screen.getByRole('button', { name: 'ทดลอง routing' }))

    expect(onSimulate).toHaveBeenCalledWith({ projectId: VALID_PROJECT_ID, amount: '2400000' })
  })

  it('prefills the Project ID field from defaultProjectId', () => {
    render(<RoutingSimulatorPanel {...defaultProps} defaultProjectId={VALID_PROJECT_ID} />)
    expect(screen.getByLabelText('รหัสโครงการ (Project ID)')).toHaveValue(VALID_PROJECT_ID)
  })

  it('shows the PaymentCertificate escalation-does-not-apply note only for that document type', () => {
    const { rerender } = render(<RoutingSimulatorPanel {...defaultProps} documentType="VariationOrder" />)
    expect(screen.queryByText(/ไม่มีเงื่อนไข escalation สะสมของ VO/)).not.toBeInTheDocument()

    rerender(<RoutingSimulatorPanel {...defaultProps} documentType="PaymentCertificate" />)
    expect(screen.getByText(/ไม่มีเงื่อนไข escalation สะสมของ VO/)).toBeInTheDocument()
  })

  it('renders a server error from a blocked simulation (e.g. PolicyGap)', () => {
    render(<RoutingSimulatorPanel {...defaultProps} error="ไม่มีเส้นทางอนุมัติที่ resolve ได้" />)
    expect(screen.getByRole('alert')).toHaveTextContent('ไม่มีเส้นทางอนุมัติที่ resolve ได้')
  })

  it('renders the resolved chain: pinned version, steps table, escalation note for a VariationOrder', () => {
    render(<RoutingSimulatorPanel {...defaultProps} result={simulation} />)

    const resultRegion = screen.getByTestId('routing-simulation-result')
    expect(within(resultRegion).getByText('v2')).toBeInTheDocument()
    expect(within(resultRegion).getByRole('table')).toBeInTheDocument()
    expect(within(resultRegion).getByText('Project Director')).toBeInTheDocument()
    expect(within(resultRegion).getByText(/มีขั้นตอน escalation เพิ่มเติม/)).toBeInTheDocument()
    // No ADR-0021 warning for a clean result.
    expect(within(resultRegion).queryByText(/ADR-0021/)).not.toBeInTheDocument()
  })

  it('states escalation never applies to a PaymentCertificate simulation, regardless of the raw flag', () => {
    render(
      <RoutingSimulatorPanel
        {...defaultProps}
        documentType="PaymentCertificate"
        result={{ ...simulation, documentType: 'PaymentCertificate', escalationApplied: false }}
      />,
    )
    const resultRegion = screen.getByTestId('routing-simulation-result')
    expect(within(resultRegion).getByText(/เงื่อนไข escalation สะสมของ VO ไม่เกี่ยวข้องกับเอกสารประเภทนี้/)).toBeInTheDocument()
  })

  it('ADR-0021: a multiple-active-policy result renders a clear, un-missable warning naming the conflicting versions', () => {
    render(
      <RoutingSimulatorPanel
        {...defaultProps}
        result={{
          ...simulation,
          multipleActivePoliciesDetected: true,
          ambiguousActivePolicies: [
            { approvalPolicyId: 'policy-2', version: 2 },
            { approvalPolicyId: 'policy-3', version: 3 },
          ],
        }}
      />,
    )

    const resultRegion = screen.getByTestId('routing-simulation-result')
    const warnings = within(resultRegion).getAllByRole('alert')
    expect(warnings[0]).toHaveTextContent('ADR-0021')
    expect(warnings[0]).toHaveTextContent('v2')
    expect(warnings[0]).toHaveTextContent('v3')
  })

  it('a fallback chain (no policy configured at all) is flagged distinctly', () => {
    render(<RoutingSimulatorPanel {...defaultProps} result={{ ...simulation, usedFallbackChain: true }} />)
    const resultRegion = screen.getByTestId('routing-simulation-result')
    expect(within(resultRegion).getByText('ใช้เส้นทางสำรอง (Fallback)')).toBeInTheDocument()
  })

  it('shows a loading spinner state on the submit button while simulating', () => {
    render(<RoutingSimulatorPanel {...defaultProps} state="simulating" />)
    expect(screen.getByRole('button', { name: 'ทดลอง routing' })).toHaveAttribute('aria-busy', 'true')
  })
})
