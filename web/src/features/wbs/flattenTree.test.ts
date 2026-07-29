import { describe, expect, it } from 'vitest'
import { collectLeafNodes, flattenWbsTree, searchWbsTree } from './flattenTree'
import type { WbsTreeNodeDto } from './types'

function node(overrides: Partial<WbsTreeNodeDto> & Pick<WbsTreeNodeDto, 'id' | 'code'>): WbsTreeNodeDto {
  return {
    parentWbsNodeId: null,
    title: overrides.code,
    weightPercentage: '0.00',
    activityCount: 0,
    children: [],
    ...overrides,
  }
}

const tree: WbsTreeNodeDto[] = [
  node({
    id: 'root-1',
    code: '1',
    children: [
      node({ id: 'child-1-1', code: '1.1', activityCount: 3 }),
      node({ id: 'child-1-2', code: '1.2', activityCount: 5, children: [node({ id: 'gc-1-2-1', code: '1.2.1', activityCount: 2 })] }),
    ],
  }),
  node({ id: 'root-2', code: '2', activityCount: 1 }),
]

describe('flattenWbsTree', () => {
  it('with nothing expanded, shows only root nodes', () => {
    const rows = flattenWbsTree(tree, new Set())
    expect(rows.map((r) => r.node.code)).toEqual(['1', '2'])
    expect(rows[0].hasChildren).toBe(true)
    expect(rows[0].isExpanded).toBe(false)
    expect(rows[1].hasChildren).toBe(false)
  })

  it('expanding a root reveals its direct children in order, at depth 1', () => {
    const rows = flattenWbsTree(tree, new Set(['root-1']))
    expect(rows.map((r) => r.node.code)).toEqual(['1', '1.1', '1.2', '2'])
    expect(rows.find((r) => r.node.code === '1.1')?.depth).toBe(1)
  })

  it('a grandchild only appears once both its parent and grandparent are expanded', () => {
    const onlyRootExpanded = flattenWbsTree(tree, new Set(['root-1']))
    expect(onlyRootExpanded.map((r) => r.node.code)).not.toContain('1.2.1')

    const bothExpanded = flattenWbsTree(tree, new Set(['root-1', 'child-1-2']))
    expect(bothExpanded.map((r) => r.node.code)).toEqual(['1', '1.1', '1.2', '1.2.1', '2'])
    expect(bothExpanded.find((r) => r.node.code === '1.2.1')?.depth).toBe(2)
  })

  it('stays flat (never throws) for a 5,000-node synthetic tree — S4-BE-01 perf scale', () => {
    const many: WbsTreeNodeDto[] = Array.from({ length: 5000 }, (_, i) =>
      node({ id: `n${i}`, code: `${i}` }),
    )
    const rows = flattenWbsTree(many, new Set())
    expect(rows).toHaveLength(5000)
  })
})

describe('collectLeafNodes', () => {
  it('returns every leaf regardless of expand state, in depth-first order', () => {
    const leaves = collectLeafNodes(tree)
    expect(leaves.map((n) => n.code)).toEqual(['1.1', '1.2.1', '2'])
  })
})

describe('searchWbsTree', () => {
  it('returns an empty list for a blank query', () => {
    expect(searchWbsTree(tree, '   ')).toEqual([])
  })

  it('matches by code or title, case-insensitively, across the whole tree regardless of expand state', () => {
    const results = searchWbsTree(tree, '1.2')
    expect(results.map((r) => r.node.code).sort()).toEqual(['1.2', '1.2.1'])
  })

  it('matches WBS node titles too (branch/zone info lives in title/code text, not a dedicated field)', () => {
    const withZoneTitle = [
      node({ id: 'z1', code: 'Z-9B', title: 'งานโครงสร้าง ชั้น 9 โซน B' }),
      node({ id: 'z2', code: 'Z-9A', title: 'งานโครงสร้าง ชั้น 9 โซน A' }),
    ]
    expect(searchWbsTree(withZoneTitle, 'โซน B').map((r) => r.node.code)).toEqual(['Z-9B'])
  })
})
