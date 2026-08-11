import { describe, expect, it } from 'vitest'
import {
  EAC_NULL_REASON_LABELS,
  EAC_VARIANT_FORMULA_LABELS,
  buildWbsNodeLabelLookup,
  describeMixedScopeNode,
  describeWarning,
  describeWeightWarning,
  selectTopCriticalActivities,
  toneForRatioThreshold,
  toneForSign,
} from './dashboardSelectors'
import type { GanttActivityDto } from '../gantt'
import type { WbsTreeNodeDto } from '../wbs'
import type { DashboardWeightWarningDto, EacNullReason, EacVariant } from './types'

const ALL_EAC_VARIANTS: EacVariant[] = ['CpiBased', 'Atypical', 'CpiSpiBased', 'BottomUpEtc', 'CustomPf']
const ALL_NULL_REASONS: EacNullReason[] = [
  'NotStarted',
  'NoActualCost',
  'NoPlannedValue',
  'ZeroCpi',
  'ManualEtcNotSet',
  'CustomPfNotSet',
]

describe('EAC_NULL_REASON_LABELS / EAC_VARIANT_FORMULA_LABELS', () => {
  it('has a non-empty Thai label for every EacNullReason the backend can return', () => {
    for (const reason of ALL_NULL_REASONS) {
      expect(EAC_NULL_REASON_LABELS[reason]).toBeTruthy()
    }
  })

  it('has a formula caption for every EacVariant, matching the DoD example shape ("BAC / CPI")', () => {
    for (const variant of ALL_EAC_VARIANTS) {
      expect(EAC_VARIANT_FORMULA_LABELS[variant]).toBeTruthy()
    }
    expect(EAC_VARIANT_FORMULA_LABELS.CpiBased).toBe('BAC / CPI')
  })
})

describe('describeWarning', () => {
  it('translates a known EVM engine warning code', () => {
    expect(describeWarning('EarnedValueExceedsBudget')).toContain('เกินงบประมาณ')
  })

  it('never hides an unmapped code — falls back to the raw code', () => {
    expect(describeWarning('SomeFutureWarningCode')).toBe('SomeFutureWarningCode')
  })
})

describe('toneForSign', () => {
  it('null -> neutral, >= 0 -> success, < 0 -> danger', () => {
    expect(toneForSign(null)).toBe('neutral')
    expect(toneForSign('0.00')).toBe('success')
    expect(toneForSign('19000000.00')).toBe('success')
    expect(toneForSign('-19000000.00')).toBe('danger')
  })
})

describe('toneForRatioThreshold', () => {
  it('null -> neutral, >= 1 -> success, < 1 -> danger (default threshold)', () => {
    expect(toneForRatioThreshold(null)).toBe('neutral')
    expect(toneForRatioThreshold('1.00')).toBe('success')
    expect(toneForRatioThreshold('1.04')).toBe('success')
    expect(toneForRatioThreshold('0.92')).toBe('danger')
  })
})

function activity(overrides: Partial<GanttActivityDto>): GanttActivityDto {
  return {
    id: 'a1',
    wbsNodeId: 'w1',
    activityCode: 'A-100',
    name: 'กิจกรรมตัวอย่าง',
    plannedStart: '2026-01-01T00:00:00+07:00',
    plannedFinish: '2026-02-01T00:00:00+07:00',
    actualStart: null,
    actualFinish: null,
    isCritical: false,
    totalFloat: 5,
    freeFloat: 5,
    ...overrides,
  }
}

describe('selectTopCriticalActivities', () => {
  it('filters to isCritical only and sorts by plannedFinish ascending (soonest due first)', () => {
    const activities: GanttActivityDto[] = [
      activity({ id: 'c-late', isCritical: true, totalFloat: 0, plannedFinish: '2026-06-01T00:00:00+07:00' }),
      activity({ id: 'not-critical', isCritical: false, plannedFinish: '2026-01-05T00:00:00+07:00' }),
      activity({ id: 'c-soon', isCritical: true, totalFloat: 0, plannedFinish: '2026-03-01T00:00:00+07:00' }),
      activity({ id: 'c-soonest', isCritical: true, totalFloat: 0, plannedFinish: '2026-02-15T00:00:00+07:00' }),
    ]

    const result = selectTopCriticalActivities(activities)

    expect(result.map((a) => a.id)).toEqual(['c-soonest', 'c-soon', 'c-late'])
  })

  it('respects the limit parameter', () => {
    const activities: GanttActivityDto[] = Array.from({ length: 10 }, (_, i) =>
      activity({ id: `c-${i}`, isCritical: true, plannedFinish: `2026-0${(i % 9) + 1}-01T00:00:00+07:00` }),
    )

    expect(selectTopCriticalActivities(activities, 4)).toHaveLength(4)
  })

  it('returns an empty array when there are no critical activities (e.g. CPM never run)', () => {
    const activities: GanttActivityDto[] = [activity({ isCritical: false }), activity({ isCritical: false })]
    expect(selectTopCriticalActivities(activities)).toEqual([])
  })

  it('never mutates the input array', () => {
    const activities: GanttActivityDto[] = [
      activity({ id: 'b', isCritical: true, plannedFinish: '2026-02-01T00:00:00+07:00' }),
      activity({ id: 'a', isCritical: true, plannedFinish: '2026-01-01T00:00:00+07:00' }),
    ]
    const original = [...activities]

    selectTopCriticalActivities(activities)

    expect(activities).toEqual(original)
  })
})

function wbsNode(overrides: Partial<WbsTreeNodeDto>): WbsTreeNodeDto {
  return {
    id: 'n1',
    parentWbsNodeId: null,
    code: 'W-01',
    title: 'โครงสร้าง',
    weightPercentage: '40.00',
    activityCount: 3,
    children: [],
    ...overrides,
  }
}

describe('buildWbsNodeLabelLookup / describeWeightWarning / describeMixedScopeNode', () => {
  const tree: WbsTreeNodeDto[] = [
    wbsNode({
      id: 'root-1',
      code: 'W-01',
      title: 'โครงสร้าง',
      children: [wbsNode({ id: 'child-1', parentWbsNodeId: 'root-1', code: 'W-01.1', title: 'ฐานราก' })],
    }),
  ]

  it('flattens the whole tree (root + nested children) into an id -> {code, title} lookup', () => {
    const lookup = buildWbsNodeLabelLookup(tree)
    expect(lookup.get('root-1')).toEqual({ code: 'W-01', title: 'โครงสร้าง' })
    expect(lookup.get('child-1')).toEqual({ code: 'W-01.1', title: 'ฐานราก' })
  })

  it('describeWeightWarning: wbsNodeId null -> root-level label; found id -> "code title"; unknown id -> the raw id', () => {
    const lookup = buildWbsNodeLabelLookup(tree)

    const rootWarning: DashboardWeightWarningDto = { wbsNodeId: null, childCount: 3, weightSum: '90.00' }
    expect(describeWeightWarning(rootWarning, lookup)).toContain('ระดับบนสุดของโครงการ')
    expect(describeWeightWarning(rootWarning, lookup)).toContain('90.00%')
    expect(describeWeightWarning(rootWarning, lookup)).toContain('3 รายการ')

    const nodeWarning: DashboardWeightWarningDto = { wbsNodeId: 'root-1', childCount: 2, weightSum: '80.00' }
    expect(describeWeightWarning(nodeWarning, lookup)).toContain('W-01 โครงสร้าง')

    const unknownWarning: DashboardWeightWarningDto = { wbsNodeId: 'ghost-id', childCount: 1, weightSum: '50.00' }
    expect(describeWeightWarning(unknownWarning, lookup)).toContain('ghost-id')
  })

  it('describeMixedScopeNode: found id -> "code title" with the real "child-subtree wins" explanation; unknown id -> the raw id', () => {
    const lookup = buildWbsNodeLabelLookup(tree)

    expect(describeMixedScopeNode('root-1', lookup)).toContain('W-01 โครงสร้าง')
    expect(describeMixedScopeNode('root-1', lookup)).toContain('หมวดงานย่อยเท่านั้น')

    expect(describeMixedScopeNode('ghost-id', lookup)).toContain('ghost-id')
  })
})
