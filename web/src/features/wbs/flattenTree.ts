import type { WbsTreeNodeDto } from './types'

export interface FlatWbsRow {
  node: WbsTreeNodeDto
  depth: number
  hasChildren: boolean
  isExpanded: boolean
}

/**
 * Flattens the nested `WbsTreeNodeDto` tree (`GetWbsTreeQuery`'s response) into the row list the
 * virtualized `DataTable` (ADR-0004) actually renders — a child only appears when every ancestor
 * up to a root is in `expandedIds`, matching the prototype's click-to-expand/collapse behaviour
 * (`docs/ECM Planning Prototype.dc.html`'s WBS screen, `r.toggle`/`r.caret`/`r.indent`).
 *
 * Pure and allocation-light (a single explicit stack, never recursion) so it stays correct and
 * fast at the S4-BE-01 perf target's scale (5,000 nodes) — this is exactly the function that runs
 * on every expand/collapse click.
 */
export function flattenWbsTree(
  rootNodes: readonly WbsTreeNodeDto[],
  expandedIds: ReadonlySet<string>,
): FlatWbsRow[] {
  const rows: FlatWbsRow[] = []

  // Reverse-push so children pop off the stack left-to-right (stacks are LIFO).
  const stack: Array<{ node: WbsTreeNodeDto; depth: number }> = [...rootNodes]
    .reverse()
    .map((node) => ({ node, depth: 0 }))

  while (stack.length > 0) {
    const { node, depth } = stack.pop()!
    const hasChildren = node.children.length > 0
    const isExpanded = hasChildren && expandedIds.has(node.id)

    rows.push({ node, depth, hasChildren, isExpanded })

    if (isExpanded) {
      for (let i = node.children.length - 1; i >= 0; i -= 1) {
        stack.push({ node: node.children[i], depth: depth + 1 })
      }
    }
  }

  return rows
}

/**
 * Flat, depth-0 search results over the *whole* tree (ignoring expand/collapse state) — the
 * "filterable by branch/zone" half of S4-FE-03's DoD (branch/zone information lives in a WBS
 * node's `code`/`title` in this domain model; `WBSNode`/`Activity` have no separate zone/team
 * field to filter on — see this sprint's frontend report). Case-insensitive substring match
 * against both `code` and `title`.
 */
export function searchWbsTree(rootNodes: readonly WbsTreeNodeDto[], query: string): FlatWbsRow[] {
  const needle = query.trim().toLowerCase()
  if (!needle) return []

  const matches: FlatWbsRow[] = []
  const stack = [...rootNodes].reverse()

  while (stack.length > 0) {
    const node = stack.pop()!

    if (node.code.toLowerCase().includes(needle) || node.title.toLowerCase().includes(needle)) {
      matches.push({ node, depth: 0, hasChildren: false, isExpanded: false })
    }

    for (let i = node.children.length - 1; i >= 0; i -= 1) {
      stack.push(node.children[i])
    }
  }

  return matches
}

/** Every leaf node (`children.length === 0`) reachable from `rootNodes`, regardless of
 * expand/collapse state — the candidate scope for "โหมดอัปเดตความคืบหน้า" node selection (a leaf
 * is the finest WBS-level grouping a user can pick before individual activities take over). */
export function collectLeafNodes(rootNodes: readonly WbsTreeNodeDto[]): WbsTreeNodeDto[] {
  const leaves: WbsTreeNodeDto[] = []
  const stack = [...rootNodes].reverse()

  while (stack.length > 0) {
    const node = stack.pop()!
    if (node.children.length === 0) {
      leaves.push(node)
    } else {
      for (let i = node.children.length - 1; i >= 0; i -= 1) {
        stack.push(node.children[i])
      }
    }
  }

  return leaves
}
