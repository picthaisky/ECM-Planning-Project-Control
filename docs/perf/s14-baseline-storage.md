# Baseline Module — Index Verification + Data-Size Estimate (S14-DB-01)

**Author:** `database-engineer` · **Date:** 2026-08-11 · **Sprint:** 14
**DoD (docs/10. §9, S14-DB-01):** index `(TenantId, BaselineId, ActivityId)`; record an estimated
size per baseline at 10,000 activities in the perf doc; unique filtered index enforcing one active
baseline per project.
**Not to be confused with** `docs/perf/baseline.md`, which is `qa-engineer`'s S4-QA-01 "performance
baseline" report for the unrelated WBS Tree API — same word, different meaning, pre-existing file,
not touched here.

**Environment constraint that shapes every claim below:** Docker Desktop cannot start on this
machine (requires Administrator; `docs/perf/gantt-frontend-s6.md` §3), so there is no SQL Server
anywhere in this environment. Nothing here was run against SQL Server. Every claim is labelled
**[EXECUTED]** (ran, on SQLite or against the real EF model's SQL-generation pipeline with no live
connection) or **[INFERRED]** (arithmetic/reasoning from the real migration/config, not run).

---

## 1. Index verification — does `(TenantId, BaselineId, ActivityId)` match the real query?

**[EXECUTED]** — `CmPlusDbContext` configured with `UseSqlServer(...)` against a connection string
that is never opened, then `.ToQueryString()` called on the exact LINQ shape
`BaselineComparisonReader.GetActivityComparisonAsync` issues (this only needs the provider's SQL
generator, not a live connection or Docker — the same offline technique `db-conventions.md` §6
cites for S2-DB-02/S11-DB-01).

Generated SQL for the bulk left-join (the query the 10,000-activity performance target actually
depends on):

```sql
SELECT [b].[ActivityId], [b].[PlannedStart], [b].[PlannedFinish], [b].[DurationDays], [b].[BudgetCost],
       [a0].[ActivityCode] AS [CurrentActivityCode], [a0].[Name] AS [CurrentName]
FROM [BaselineActivitySnapshots] AS [b]
LEFT JOIN (
    SELECT [a].[Id], [a].[ActivityCode], [a].[Name]
    FROM [Activities] AS [a]
    WHERE [a].[TenantId] = @ef_filter__TenantId
) AS [a0] ON [b].[ActivityId] = [a0].[Id]
WHERE [b].[TenantId] = @ef_filter__TenantId AND [b].[BaselineId] = @baselineId
```

**Verdict: confirmed.** The predicate on `BaselineActivitySnapshots` is exactly
`TenantId = @t AND BaselineId = @baselineId` — the two leading columns of
`IX_BaselineActivitySnapshots_TenantId_BaselineId_ActivityId`, so this is a leading-prefix seek,
not a scan. `ActivityId` (the index's third column) isn't part of the predicate, but it does the
job the migration's comment already states: it makes the index **unique**, enforcing "one snapshot
row per (baseline, activity)" at the DB level, and it happens to make the index fully covering for
this query's snapshot-side columns... except it is not fully covering — `PlannedStart`,
`PlannedFinish`, `DurationDays`, `BudgetCost` are not in the index, so this is a seek + key lookup
(or, more likely, SQL Server will choose the clustered PK directly once it has all four scalar
payload columns to fetch anyway) — a seek either way, never a scan. The join side
(`Activities`, keyed on its PK `Id`) is a per-row nested-loop seek against the clustered PK,
efficient at the 10,000-row bound. **No index change needed; the shipped index is correct for the
real query.**

Two more read queries checked opportunistically (both `IBaselineComparisonReader` methods):

```sql
-- FindActiveBaselineAsync
WHERE [b].[TenantId] = @ef_filter__TenantId AND [b].[ProjectId] = @projectId AND [b].[IsActive] = CAST(1 AS bit)
-- FindBaselineAsync
WHERE [b].[TenantId] = @ef_filter__TenantId AND [b].[ProjectId] = @projectId AND [b].[Id] = @baselineId
```

`FindActiveBaselineAsync`'s predicate is an exact match for the filtered index's own filter
(`WHERE IsActive = 1`) plus its key (`TenantId, ProjectId`) — the DoD's second index (§3 below)
also serves this read for free, a nice bonus, not something that needed a separate index.
`FindBaselineAsync` filters on `Id` (the clustered PK) with `ProjectId` as a residual predicate — a
PK seek, no new index needed.

**What this does not prove:** an actual execution plan (`Seek` vs `Scan` badge, real cost) — that
needs a live SQL Server and is explicitly out of reach here (`docs/db-conventions.md` §6: "'Index
seek' DoDs are literal... verified with an actual execution plan... before merge"). The verification
steps for whoever has SQL Server available:

```sql
SET STATISTICS XML ON;
-- run with a real @baselineId at 10,000-row scale (tests/perf/seed-large-project.sql's pattern,
-- extended with a Baseline + 10,000 BaselineActivitySnapshot rows)
SELECT ... -- the exact query above
-- confirm the plan shows Index Seek on IX_BaselineActivitySnapshots_TenantId_BaselineId_ActivityId,
-- not a Scan, and Estimated/Actual rows ≈ 10,000, not the full table.
```

---

## 2. Unique filtered index (one active baseline per project) — does the ordering mitigation hold?

`BaselineConfiguration` ships `IX_Baselines_TenantId_ProjectId_Active` on `(TenantId, ProjectId)
WHERE IsActive = 1`, mirroring `ApprovalPolicy`. `Baseline.cs`/`ActivateBaselineCommandHandler.cs`'s
own doc comments already flag the open question precisely: does EF Core reliably emit the
*deactivate* UPDATE before the *activate* UPDATE within one `SaveChanges` batch, given the handler
calls `previousActive?.Deactivate(); target.Activate();` in that order? Sprint 10 found "strong
evidence" of safety for `ApprovalPolicy` using SQLite as a constraint-enforcing stand-in, but
flagged it as signal, not proof, and could not test `ApprovalPolicy`'s nullable `ProjectId` case.
`Baseline.ProjectId` is `NOT NULL`, so that specific caveat doesn't apply here — worth repeating the
technique. **No artifact of the Sprint 10 probe exists in this repo** (scratch probes are
session-local by convention/environment), so this is a fresh, independent run, not a re-read of an
old result.

**[EXECUTED].** Real `Baseline`/`BaselineActivitySnapshot` entities + real
`BaselineConfiguration`/`BaselineActivitySnapshotConfiguration` (unmodified), hosted in a minimal
probe `DbContext` against a real, on-disk SQLite database (a genuine, constraint-enforcing,
non-deferred-check relational engine — SQLite, like SQL Server, checks a unique index at statement
time, not at commit). Confirmed first that the filtered index actually materializes on SQLite
(`Database.GenerateCreateScript()`):

```sql
CREATE UNIQUE INDEX "IX_Baselines_TenantId_ProjectId_Active" ON "Baselines" ("TenantId", "ProjectId") WHERE [IsActive] = 1;
```

**Test:** 30 independent trials. Each trial seeds a brand-new project with two fresh baselines (one
active, one not — so trials cannot interact through the shared unique index or leftover state), then
reproduces `ActivateBaselineCommandHandler.Handle`'s *exact* call order verbatim: `target =
FindAsync(...)` loaded first, `previousActive = FindActiveAsync(...)` loaded second,
`previousActive?.Deactivate()` called first, `target.Activate()` called second, one
`SaveChangesAsync()`. EF Core's own `CommandExecuting` diagnostic event (fires before the outcome of
a statement is known, so it reports the true attempted order even for the statement that goes on to
fail) recorded which UPDATE SQL Server... SQLite actually attempted first.

**Result: this is not a re-confirmation. It is the opposite finding, and it is decisive:**

| First UPDATE attempted | Trials | SaveChanges outcome |
| --- | --: | --- |
| `previousActive` (deactivate) | 16/30 | **SUCCESS**, every time |
| `target` (activate) | 14/30 | **FAILED**, every time — `SQLite Error 19: UNIQUE constraint failed` |

The correlation is 1:1 in both directions across all 30 trials: whichever UPDATE is attempted first
determines success or failure, with no exception. But **which one goes first is not controlled by
the handler's code at all** — across 30 trials with byte-for-byte identical call order and mutation
order, the split was close to a coin flip (16/14), and it varied trial to trial despite every trial
running the identical code path. A second, smaller run (30 trials with all-verbose logging, a
different random seed) produced 12/30 vs 18/30 — a different split, same conclusion. **The
"call `Deactivate()` before `Activate()`" discipline the code comments describe as a mitigation
provides no actual protection for this entity pair.**

Why this differs from the `ApprovalPolicy` result: `UpdateApprovalPolicyCommandHandler` loads
`current` (the row being deactivated) **first**, then constructs the brand-new `nextVersion` row
(state `Added`, not `Modified`) **second** — tracking order and mutation order agree, and the pair
is `Modified` + `Added`, not two `Modified` siblings. `ActivateBaselineCommandHandler` loads
`target` (the row being activated) **first** and `previousActive` (being deactivated) **second** —
the 404/authorization check on `target` has to happen before it's even known whether a
`previousActive` lookup is needed at all, so the natural code shape inverts the tracking order
relative to `ApprovalPolicy`, and both rows are `Modified`. This is consistent with EF Core's
statement-batch ordering for two unrelated (`no FK relationship`) `Modified` entries not being a
promise the application can rely on — apparently governed by something closer to the entries'
internal iteration order in the change tracker than by anything visible in application code. The
sort that produces this order runs in EF Core's provider-agnostic `Update` pipeline, before
SQL text is generated, so there is no reason to expect SQL Server would order these two statements
any more predictably than SQLite did — but that remains **[INFERRED]**, not run, and is exactly what
§2.1 below asks a future SQL Server run to confirm or refute.

**Practical consequence if unfixed:** on a real database, roughly half of all `Activate` calls where
a previous active baseline genuinely exists would fail with an unhandled `DbUpdateException` →
`GlobalExceptionHandler`'s generic 500 (`BaselineRepository.TrySaveChangesAsync`'s own doc comments
already note this path is not specially handled, inherited from `ApprovalPolicy`'s identical gap).
That is not a rare edge case — it is the **ordinary case**: every activation after the first one for
a given project has a previous active row to race against.

**Verified fix — [EXECUTED], 30/30 succeeded.** Splitting the single batched `SaveChanges` into
**two sequential `SaveChangesAsync` calls inside one transaction** removes the non-determinism
entirely, because each call is its own fully-flushed round trip — the deactivate is durable within
the transaction before the activate statement is even generated, so there is nothing left for EF's
internal ordering to get wrong:

```csharp
await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken);
previousActive?.Deactivate();
await dbContext.SaveChangesAsync(cancellationToken);   // (1) alone, first
target.Activate();
await dbContext.SaveChangesAsync(cancellationToken);   // (2) alone, second
await tx.CommitAsync(cancellationToken);
```

Re-ran the identical 30-trial harness with this shape substituted for the single-`SaveChanges` call:
**30/30 succeeded**, zero constraint violations. This is a genuine fix, not merely a workaround
under SQLite specifically — it removes the actual mechanism at fault (relying on EF's unordered
batch of two `Modified` siblings) rather than papering over one provider's behaviour.

**This is a finding, not merely a re-verification, and needs a code change:**
`ActivateBaselineCommandHandler`/`IBaselineRepository.TrySaveChangesAsync` currently document the
single-`SaveChanges` "deactivate-before-activate" ordering as the (unproven-but-assumed-safe)
mitigation. That assumption is now empirically disproven under a real constraint-enforcing engine,
with a concrete, tested alternative available. **Recommend `backend-developer` change
`ActivateBaselineCommandHandler` to two sequential `SaveChanges` calls in one transaction** (shown
above) and update its own doc comments and `IBaselineRepository`'s remarks accordingly; this is an
Application-layer change and stays out of `database-engineer`'s lane to make directly. The DB-level
half of the mitigation — the filtered unique index itself — is correct and necessary regardless of
which fix lands in the handler; nothing here changes the recommendation to keep it.

### 2.1 What a future SQL Server run should do to close the gap
```sql
-- Two sessions/threads is not even needed - a single serial reproduction is enough, since the
-- finding is about single-batch statement ordering, not concurrency:
-- 1. Seed a project with baseline A (active), B (inactive).
-- 2. Run ActivateBaselineCommandHandler's real code path against SQL Server ~30 times, alternating
--    the activation target, and record success/failure exactly as this probe did.
-- 3. If any run fails with "Cannot insert duplicate key row... IX_Baselines_TenantId_ProjectId_Active",
--    that confirms the SQLite finding transfers to SQL Server unchanged - apply the two-SaveChanges fix.
-- 4. If 30/30 succeed, SQL Server's own command-batching heuristic happens to differ from SQLite's -
--    interesting, but does not make the current code *correct*, only *lucky on this data shape*;
--    still recommend the fix, since nothing in EF's public contract guarantees the lucky ordering
--    persists across EF Core versions or larger batches.
```

---

## 3. Data-size estimate per baseline at 10,000 activities

**[INFERRED]** — computed from the real column types in
`20260811100442_Sprint14_Baseline.cs`/`artifacts/migrations/20260811_sprint14_baseline.sql`
(confirmed identical) using SQL Server's published fixed-length-type storage sizes and the standard
row-size formula (`Fixed_Data_Size + Variable_Data_Size + Null_Bitmap(2 + ceil(cols/8)) + 4 bytes
row header`). No live database to measure `sys.dm_db_partition_stats` against, so this is arithmetic,
not a measurement — flagged as such throughout.

### 3.1 `BaselineActivitySnapshots` — one row per activity in a baseline

All eight columns are fixed-length (no `nvarchar`/`varbinary` on this table):

| Column | SQL type | Bytes |
| --- | --- | --: |
| Id | uniqueidentifier | 16 |
| TenantId | uniqueidentifier | 16 |
| BaselineId | uniqueidentifier | 16 |
| ActivityId | uniqueidentifier | 16 |
| PlannedStart | datetimeoffset(7) | 10 |
| PlannedFinish | datetimeoffset(7) | 10 |
| DurationDays | int | 4 |
| BudgetCost | decimal(18,2) | 9 |
| **Fixed data subtotal** | | **97** |

Row overhead: null bitmap `2 + ceil(8/8) = 3` bytes, no variable-length-column prefix (none exist),
`+4` bytes row header → **≈ 104 bytes/row** in the clustered index (PK on `Id`), **+2 bytes** page
slot-array entry → **≈ 106 bytes/row** delivered.

Two nonclustered indexes also carry a copy of their key columns + the clustering key (`Id`, needed
as the row locator since neither index includes `Id` in its own key):

| Index | Key bytes | + Id locator | + overhead (null bitmap+header+slot, ≈10) | ≈ bytes/row |
| --- | --: | --: | --: | --: |
| `IX_..._BaselineId` (EF's auto FK index — the known, accepted, already-documented redundant pattern, `db-conventions.md` §6) | 16 | 16 | 10 | **42** |
| `IX_..._TenantId_BaselineId_ActivityId` (unique — the DoD index) | 48 | 16 | 10 | **74** |

**Per-baseline total at 10,000 activities:**

```
Base table:            10,000 × 106 bytes ≈ 1.01 MB
IX_..._BaselineId:      10,000 ×  42 bytes ≈ 0.40 MB
IX_..._TenantId_..._ActivityId: 10,000 × 74 bytes ≈ 0.71 MB
                                              -----------
Raw logical data                             ≈ 2.12 MB
+ ~15% page-fill/slack allowance             ≈ 2.4 MB  <- headline number
```

The parent `Baselines` row itself (one per baseline, not per activity: 4 GUIDs + a short `nvarchar`
name + `bit` + `datetimeoffset(7)` + `decimal(18,2)` ≈ 150–200 bytes, plus a near-empty filtered
index that only ever holds one row per *project*, not per baseline) is negligible next to the
snapshot table and is not broken out further.

**Sanity check against `docs/db-conventions.md`'s own already-published estimates:** `ADR-0009`
puts `ActivityProgressLog` at ~350k rows/project (8 months × weekly × 10,000 activities), and
`ADR-0019`/domain-rules.md puts `CpmRun` history at ~875k rows/project over a project's life. A
single Baseline capture (10,000 rows, ≈2.4 MB) is **two to three orders of magnitude smaller** than
either of those per-project totals — consistent with Baseline being a deliberate, occasional,
human-initiated save point (like a git tag), not a per-recalculation/per-period artifact like
`CpmRun`/`ActivityProgressLog`.

### 3.2 Growth per project per year — the number the DoD actually wants

One row count means nothing on its own (the DoD's own framing: baselines are captured repeatedly
over a project's life). **No sprint doc states a capture cadence** — this is not a formula
`domain-expert` has ruled on, so the figure below is an assumption, made explicit rather than
silently baked in. The only concrete anchor in this repo is the prototype's own worked example
(`docs/ECM Planning Prototype.dc.html`, `blData`): three named baselines — `BL-0 Original Baseline
(สัญญา)` (locked permanently), `BL-1 Revised Baseline — Rev.2 (+VO)`, `BL-2 What-if — เร่งงานโครงสร้าง`
— spanning 1 มิ.ย. 2568 to 10 ก.ค. 2569, i.e. **≈3 captures in ≈13 months**, driven by contract
events (original signing, a VO-triggered revision, an exploratory what-if), not a fixed schedule.

| Cadence assumption | Captures/year | MB/project/year (× 2.4 MB) |
| --- | --: | --: |
| Prototype's own worked example | ≈2.8 | ≈6.7 MB |
| Conservative (quarterly re-baseline) | 4 | ≈9.6 MB |
| Heavy VO-churn project (monthly) | 12 | ≈28.8 MB |

**What this implies:** even the heavy-churn upper bound is trivial storage — a rounding error next
to `ActivityProgressLog`'s ~350k rows/project or `CpmRun`'s ~875k rows/project. **This table does
not need a storage-driven retention policy at any volume this product is realistically going to see**
(see §4). The number worth remembering is qualitative, not the megabytes: baseline growth is
*event-triggered and human-paced*, not *system-paced* — the opposite growth shape from every other
append-only table this schema has needed to worry about.

---

## 4. Retention — does `CpmRun`'s citation-aware reasoning apply to `BaselineActivitySnapshot`?

`BaselineActivitySnapshot` is `IAppendOnly`, captured per-activity per-baseline, structurally the
same shape as `CpmRunActivity` (ADR-0019, `db-conventions.md` §7.1: retention must be
citation-aware, since pruning a run an `EotEvaluation` cites would silently invalidate that
evaluation). Worth checking whether the same reasoning transfers — **it partially does, as a
principle, but the concrete conclusion is different, and in one respect opposite.**

**What transfers:** the general rule that an append-only table backing a comparison/evidentiary
feature must not be pruned by age alone, and that "is this row cited by something durable" is the
right question to ask before deleting anything — confirmed genuinely relevant, not dismissed.

**What does not transfer, and why a `CpmRun`-shaped retention job would be the wrong design here:**

1. **No storage pressure exists to motivate one.** §3.2 above: even a pessimistic heavy-churn
   project accumulates tens of MB/year, not hundreds of thousands of rows. `CpmRun`'s retention
   sketch exists because *something* has to eventually prune ~875k rows/project; nothing here
   reaches a scale where pruning is the answer to a real problem.
2. **There is currently nothing that durably cites a specific `BaselineId` the way
   `EotEvaluationRun` cites a specific `CpmRunId`.** Checked directly — no table in this schema has
   a `BaselineId` foreign key besides `BaselineActivitySnapshot` itself, and
   `CompareBaselineQueryHandler` computes its comparison **live** on every call (its own remarks:
   "never inside the LINQ projection... after materialization"), never freezing a stored result the
   way `EvmPeriodSnapshot` freezes a closed EVM period. So a citation-check today would have nothing
   to check against — building a pruning job now would be flying blind, exactly the trap
   `db-conventions.md` §7.1 already names for `CpmRun` ("nobody has built this... needs to turn it
   into an actual design doc").
3. **The more important asymmetry: unlike `CpmRun`, a `Baseline` is not disposable derived data —
   it is itself the citable, sometimes-permanently-significant artifact.** `CpmRun` rows exist only
   to *serve* other queries (criticality-as-of-a-date); once nothing cites a given run and it isn't
   the project's latest, it has no standalone value. A `Baseline` is the opposite: the prototype's
   own `BL-0 Original Baseline (สัญญา)` is explicitly "🔒 ล็อกถาวร" (locked permanently) — the
   *contract* baseline is a document with standalone legal/contractual significance for the life of
   the project (and potentially into a post-completion dispute) regardless of whether anything in
   this database currently "cites" it, and regardless of whether it is the *active* baseline right
   now. `CpmRun`'s "never the latest run" exemption has no equivalent rule that would safely
   identify *which* inactive baseline is safe to prune — "inactive" here means "not the current
   comparison target," never "no longer significant," and the schema has no field distinguishing an
   `Original`/contract baseline from a disposable `What-if` draft (`Baseline.Name` is free text).
   Automating that distinction from data alone is not safe.

**Conclusion:** do not build a `Baseline`/`BaselineActivitySnapshot` retention job on the `CpmRun`
model. If a retention policy is ever wanted (e.g. purging old draft "what-if" baselines a user
explicitly marks disposable), it needs (a) a real citation table once something durably references
a `BaselineId` — none exists today — and (b) a typed field distinguishing a baseline's role/
permanence (e.g. `Baseline.Kind ∈ {Original, Revised, WhatIf}` or an explicit `IsLocked`), which is
a product/domain decision, not something `database-engineer` should infer or add unilaterally. Until
then, the correct posture is simply: keep every row, because nothing forces the alternative and
nothing safely tells the difference between "safe to prune" and "the contract baseline."

---

## 5. Migrations — what could and could not be verified

**[EXECUTED]**: `dotnet build backend/CMPlus.sln -c Release` → **0 warnings, 0 errors**.
`dotnet test` per project (never summed): **Domain 406/406, Application 680/680, Architecture
14/14, Integration 544/544** — all pass, matching the expected baseline exactly.

**[NOT EXECUTED — no SQL Server in this environment]**: `dotnet ef database update`, any real
execution plan, any `sys.indexes`/`sys.dm_db_partition_stats` measurement, the 30-trial ordering
probe re-run against SQL Server (§2.1). The exported idempotent script
(`artifacts/migrations/20260811_sprint14_baseline.sql`) was inspected and matches the migration
exactly, including EF's `EXEC(N'CREATE UNIQUE INDEX ... WHERE [IsActive] = 1')` wrapping for the
filtered index — this is the same filtered-index shape `db-conventions.md` §5 already documents the
operational precondition for (`sqlcmd -I`, capital I, or an ADO.NET-based runner — never bare
`sqlcmd -i`); no new precondition, this migration just needs the same already-documented care every
filtered index in this schema needs since Sprint 2.
