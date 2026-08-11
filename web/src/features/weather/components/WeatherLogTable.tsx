import { useMemo } from 'react'
import { Button, DataTable, StatusPill } from '../../../components'
import type { DataTableColumn } from '../../../components'
import { formatRatio } from '../../../utils/format'
import { buildWeatherChainInfo } from '../weatherChain'
import type { WeatherChainState } from '../weatherChain'
import { WEATHER_CONDITION_LABELS, WEATHER_ENTRY_KIND_LABELS, WEATHER_IMPACT_LABELS, formatWeatherDate } from '../weatherLabels'
import type { WeatherLogDto } from '../types'

export interface WeatherLogTableProps {
  logs: WeatherLogDto[]
  state: 'loading' | 'ready' | 'error'
  errorMessage?: string
  /** When true, only rows with an in-force, unattributed stoppage are shown — wired from
   * `WeatherSummaryTiles`'s "ยื่นบันทึกแก้ไขเพื่อระบุกิจกรรม" prompt. */
  onlyUnattributed?: boolean
  /** Role-gated by the caller (mirrors `ProjectWeatherLogsController.WriteRoles`) — when false, the
   * "แก้ไข/ยกเลิกรายการ" action never renders regardless of chain state. */
  canWrite: boolean
  onRequestCorrection: (target: WeatherLogDto) => void
}

const CHAIN_STATE_PILL: Record<WeatherChainState, { label: string; tone: 'success' | 'neutral' | 'danger' }> = {
  effective: { label: 'ปัจจุบัน (มีผล)', tone: 'success' },
  corrected: { label: 'ถูกแก้ไขแล้ว', tone: 'neutral' },
  retracted: { label: 'ถูกเพิกถอน', tone: 'danger' },
}

/**
 * The weather log register (S11-FE-01, prototype screen `docs/ECM Planning Prototype.dc.html`
 * ~line 429 "บันทึกสภาพอากาศรายวัน") on the virtualized `DataTable` primitive (ADR-0004) — its own
 * doc comment names this exact table ("VO/Issue/Weather logs") as an intended consumer.
 *
 * Unlike the prototype's flat table, this one also renders the correction-chain state
 * (`weatherChain.ts`) — the prototype had no concept of corrections at all, but domain-rules.md
 * §8.2 makes the chain load-bearing (a superseded row must never be mistaken for current evidence),
 * so a "สถานะบันทึก" column and a per-row "แก้ไข/ยกเลิกรายการ" action are added. The action only ever
 * appears on the current chain tail (`isChainTail`), mirroring the server's own §8.2 rule 2/3
 * precondition — offering it elsewhere would just produce a 409 `WeatherLogAlreadySuperseded`.
 */
export function WeatherLogTable({ logs, state, errorMessage, onlyUnattributed, canWrite, onRequestCorrection }: WeatherLogTableProps) {
  const chainInfo = useMemo(() => buildWeatherChainInfo(logs), [logs])

  const rows = useMemo(() => {
    if (!onlyUnattributed) return logs
    return logs.filter((log) => {
      const info = chainInfo.get(log.id)
      return info?.inForce && log.impact !== 'NoImpact' && log.affectedActivityIds.length === 0
    })
  }, [logs, chainInfo, onlyUnattributed])

  const columns: DataTableColumn<WeatherLogDto>[] = [
    {
      key: 'date',
      header: 'วันที่',
      width: 96,
      render: (row) => <span className="text-text">{formatWeatherDate(row.logDate)}</span>,
    },
    {
      key: 'condition',
      header: 'สภาพอากาศ',
      width: 160,
      render: (row) => (
        <div className="truncate">
          <div className="text-text-muted">{WEATHER_CONDITION_LABELS[row.condition]}</div>
          {row.conditionNote && <div className="truncate text-[10.5px] text-text-faint">{row.conditionNote}</div>}
        </div>
      ),
    },
    {
      key: 'rainfall',
      header: 'ปริมาณฝน (มม.)',
      width: 100,
      align: 'right',
      render: (row) => <span className="text-text-muted">{row.rainfallMm === null ? '—' : formatRatio(row.rainfallMm, 2)}</span>,
    },
    {
      key: 'impact',
      header: 'ผลกระทบ',
      render: (row) => {
        const info = chainInfo.get(row.id)
        const unattributed = row.impact !== 'NoImpact' && row.affectedActivityIds.length === 0
        return (
          <div className="truncate">
            <div className="text-text-muted">{row.impactNote ?? WEATHER_IMPACT_LABELS[row.impact]}</div>
            <div className="flex items-center gap-1 text-[10.5px] text-text-faint">
              {row.affectedActivityIds.length > 0 && <span>{row.affectedActivityIds.length} กิจกรรม</span>}
              {unattributed && info?.inForce && (
                <span className="text-warning-text">ยังไม่ได้ระบุกิจกรรมที่ได้รับผลกระทบ — ยื่นบันทึกแก้ไขเพื่อระบุ</span>
              )}
            </div>
          </div>
        )
      },
    },
    {
      key: 'stop',
      header: 'หยุดงาน',
      width: 96,
      align: 'right',
      render: (row) =>
        row.workStoppage ? <StatusPill label="หยุดงาน" tone="danger" /> : <StatusPill label="ทำงานปกติ" tone="success" />,
    },
    {
      key: 'chain',
      header: 'สถานะบันทึก',
      width: 148,
      render: (row) => {
        const info = chainInfo.get(row.id)
        const pill = CHAIN_STATE_PILL[info?.state ?? 'effective']
        return (
          <div>
            <StatusPill label={pill.label} tone={pill.tone} />
            <div className="mt-0.5 text-[10.5px] text-text-faint">{WEATHER_ENTRY_KIND_LABELS[row.entryKind]}</div>
          </div>
        )
      },
    },
    {
      key: 'actions',
      header: 'การดำเนินการ',
      width: 150,
      align: 'right',
      render: (row) => {
        const info = chainInfo.get(row.id)
        const correctable = canWrite && !!info?.isChainTail && row.entryKind !== 'Retraction'
        if (!correctable) return null
        return (
          <Button size="sm" variant="secondary" onClick={() => onRequestCorrection(row)}>
            แก้ไข/ยกเลิกรายการ
          </Button>
        )
      },
    },
  ]

  return (
    <DataTable
      columns={columns}
      rows={rows}
      getRowId={(row) => row.id}
      state={state}
      errorMessage={errorMessage}
      emptyMessage={onlyUnattributed ? 'ไม่มีบันทึกที่ยังไม่ระบุกิจกรรม' : 'ยังไม่มีบันทึกสภาพอากาศในโครงการนี้'}
      rowHeight={56}
      height={440}
      ariaLabel="บันทึกสภาพอากาศรายวัน"
    />
  )
}
