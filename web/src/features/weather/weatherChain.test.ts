import { describe, expect, it } from 'vitest'
import { buildWeatherChainInfo, effectiveWeatherLogs } from './weatherChain'
import type { WeatherLogDto } from './types'

function makeLog(overrides: Partial<WeatherLogDto> & { id: string }): WeatherLogDto {
  return {
    projectId: 'project-1',
    logDate: '2026-07-08T00:00:00Z',
    condition: 'HeavyRain',
    conditionNote: null,
    rainfallMm: '61.00',
    impact: 'FullStoppage',
    impactNote: null,
    hoursLost: '8.00',
    workStoppage: true,
    entryKind: 'Original',
    correctsWeatherLogId: null,
    correctionReason: null,
    affectedActivityIds: ['activity-1'],
    recordedByUserId: 'user-1',
    recordedAt: '2026-07-08T09:00:00Z',
    ...overrides,
  }
}

describe('features/weather/weatherChain', () => {
  it('marks a lone Original entry as effective and a correctable chain tail', () => {
    const logs = [makeLog({ id: 'e1' })]
    const chain = buildWeatherChainInfo(logs)

    expect(chain.get('e1')).toMatchObject({
      inForce: true,
      state: 'effective',
      supersededBy: null,
      isChainTail: true,
    })
  })

  // domain-rules.md weather-eot fixture W-10: E1 original -> E2 corrects E1 -> Leff = {E2}.
  it('marks the original as corrected (not in force) once a Correction points at it', () => {
    const e1 = makeLog({ id: 'e1', entryKind: 'Original' })
    const e2 = makeLog({ id: 'e2', entryKind: 'Correction', correctsWeatherLogId: 'e1', correctionReason: 'typo' })
    const chain = buildWeatherChainInfo([e1, e2])

    expect(chain.get('e1')).toMatchObject({ inForce: false, state: 'corrected', isChainTail: false })
    expect(chain.get('e1')?.supersededBy?.id).toBe('e2')

    expect(chain.get('e2')).toMatchObject({ inForce: true, state: 'effective', isChainTail: true })
    expect(chain.get('e2')?.correctsTarget?.id).toBe('e1')
  })

  // W-10 step 8/9: a second correction must target the current tail (E2), not the original (E1).
  it('exposes isChainTail=false for the original once superseded, so only the tail is offered for correction', () => {
    const e1 = makeLog({ id: 'e1' })
    const e2 = makeLog({ id: 'e2', entryKind: 'Correction', correctsWeatherLogId: 'e1', correctionReason: 'r' })
    const chain = buildWeatherChainInfo([e1, e2])

    expect(chain.get('e1')?.isChainTail).toBe(false)
    expect(chain.get('e2')?.isChainTail).toBe(true)
  })

  // W-10 step 10: a Retraction removes BOTH itself and its target from L_eff.
  it('removes both the retraction and its target from the effective set', () => {
    const e1 = makeLog({ id: 'e1' })
    const e2 = makeLog({ id: 'e2', entryKind: 'Retraction', correctsWeatherLogId: 'e1', correctionReason: 'wrong date' })
    const chain = buildWeatherChainInfo([e1, e2])

    expect(chain.get('e1')).toMatchObject({ inForce: false, state: 'retracted' })
    expect(chain.get('e2')).toMatchObject({ inForce: false, state: 'retracted' })
  })

  it('is total: an id absent from the loaded list never appears as a crash, only as null links', () => {
    const e2 = makeLog({ id: 'e2', entryKind: 'Correction', correctsWeatherLogId: 'missing', correctionReason: 'r' })
    const chain = buildWeatherChainInfo([e2])

    expect(chain.get('e2')?.correctsTarget).toBeNull()
  })

  it('effectiveWeatherLogs returns exactly the in-force subset (W-10 full chain)', () => {
    const e1 = makeLog({ id: 'e1' })
    const e2 = makeLog({ id: 'e2', entryKind: 'Correction', correctsWeatherLogId: 'e1', correctionReason: 'r1', hoursLost: '3.00' })
    const e3 = makeLog({ id: 'e3', entryKind: 'Correction', correctsWeatherLogId: 'e2', correctionReason: 'r2', hoursLost: '7.00' })

    expect(effectiveWeatherLogs([e1, e2, e3]).map((l) => l.id)).toEqual(['e3'])
    expect(effectiveWeatherLogs([e1, e2]).map((l) => l.id)).toEqual(['e2'])
    expect(effectiveWeatherLogs([e1]).map((l) => l.id)).toEqual(['e1'])
  })

  it('a second correction targeting the already-superseded entry does not change the tail (defensive: first match wins deterministically)', () => {
    // The backend's own unique index (§8.2 rule 2) prevents this in practice; this only asserts the
    // client-side derivation degrades deterministically rather than throwing if it ever saw it.
    const e1 = makeLog({ id: 'e1' })
    const e2 = makeLog({ id: 'e2', entryKind: 'Correction', correctsWeatherLogId: 'e1', correctionReason: 'r1' })
    const e2b = makeLog({ id: 'e2b', entryKind: 'Correction', correctsWeatherLogId: 'e1', correctionReason: 'r2' })
    const chain = buildWeatherChainInfo([e1, e2, e2b])

    expect(chain.get('e1')?.supersededBy?.id).toBe('e2')
  })
})
