/**
 * Wire shapes for S11-FE-01's Issue / Action Log screen (US-11.2), transcribed from the real
 * source: `backend/src/CMPlus.Application/Features/Issues/IssueLogDto.cs`,
 * `.../Issues/Queries/ListIssues/{IssueListResultDto,ListIssuesQuery}.cs`,
 * `.../Issues/Commands/CreateIssue/CreateIssueCommand.cs`,
 * `backend/src/CMPlus.WebApi/Controllers/Issues/IssueRequests.cs`,
 * `backend/src/CMPlus.Domain/Enums/IssueStatus.cs`.
 */

/** `CMPlus.Domain.Enums.IssueStatus` — `Open -> Doing -> Closed`, one step at a time
 * (domain-rules.md weather-eot §9.1). `Closed` is terminal; no reopen (a recurrence is a new issue
 * carrying `RelatedIssueId`, not modelled on the wire yet this sprint). */
export type IssueStatus = 'Open' | 'Doing' | 'Closed'

/**
 * `IssueLogDto` — the shared response shape for `GET .../issues` (one item), `POST .../issues`, and
 * `POST .../issues/{id}/advance-status`. `sequenceNo` is presentational only (oldest = 1, "NOT a
 * persisted business key" per the backend's own doc comment) and is `null` on the two mutation
 * responses (`CreateIssueCommand`/`AdvanceIssueStatusCommand` both construct it with
 * `sequenceNo: null` — only `ListIssuesQueryHandler` computes the real value) — never trust a
 * mutation response's `sequenceNo` for display; always re-derive it from the freshly reloaded list
 * (see `useIssueActions.ts`'s "reload after mutate" rule).
 */
export interface IssueLogDto {
  id: string
  projectId: string
  sequenceNo: number | null
  title: string
  detail: string | null
  owner: string | null
  dueDate: string | null
  status: IssueStatus
  startedAt: string | null
  closedAt: string | null
  createdByUserId: string
  createdAt: string
}

/** `IssueStatusCountsDto` — always `open + doing + closed === totalCount`, computed server-side over
 * the same unpaginated, identically-filtered row set as `items` (domain-rules.md §9.3). This screen
 * never derives these three numbers from `items` itself — see `IssueSummaryTiles.tsx`'s own remarks,
 * the DoD's explicit "tile counts must match the table" requirement. */
export interface IssueStatusCountsDto {
  open: number
  doing: number
  closed: number
}

/**
 * `IssueListResultDto` — `GET /api/v1/projects/{projectId}/issues` response body.
 *
 * **Disclosed backend gap (domain-rules.md §9.3's own remarks, verified from source):**
 * `ListIssuesQueryHandler` returns the full, unpaginated, unfiltered project issue list —
 * `totalCount` always equals `items.length` today. The DoD's tile/table-consistency guarantee still
 * holds regardless (both are derived from the same in-memory row set server-side), but there is no
 * `page`/`pageSize`/`status=`/`owner=` query support yet despite domain-rules.md §9.3's example
 * response showing it. This frontend does not fake pagination against a backend that does not
 * support it — see `IssuePage.tsx`'s own remarks: any filtering/search here is client-side, over the
 * already-fully-loaded `items`, and is presented as such (no "page 1 of N" UI implying a partial
 * fetch).
 */
export interface IssueListResultDto {
  items: IssueLogDto[]
  totalCount: number
  statusCounts: IssueStatusCountsDto
}

/** `CreateIssueRequest` — `POST /api/v1/projects/{projectId}/issues`. Always created `Status = Open`
 * server-side (US-11.2); there is no `status` field to set here. */
export interface CreateIssuePayload {
  title: string
  detail: string | null
  owner: string | null
  dueDate: string | null
}
