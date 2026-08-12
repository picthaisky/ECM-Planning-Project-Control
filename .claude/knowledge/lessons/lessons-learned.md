# Lessons Learned

Append-only, newest first. Written by knowledge-curator (or via `/learn`).
Every entry must end with an actionable rule. QA turns recurring lessons into permanent tests.

Entry format:

```
## YYYY-MM-DD — <short title>
Context: <task/feature>
What happened: <the failure or correction, 1–3 lines>
Root cause: <why>
Rule: <what every agent does differently next time>
```

---

## 2026-07-11 — Knowledge base initialized
Context: Multi-agent system bootstrap from docs/1–8.
What happened: Team, skills, and knowledge base created from the product documentation.
Root cause: —
Rule: Agents consult INDEX.md before non-trivial work; work that reveals a gap in this
knowledge base must end with a `/learn` capture so the gap closes permanently.

## 2026-07-12 — Design system was built from prose before a working prototype existed
Context: The team's first pass at `/cmplus-ui` and `CLAUDE.md` encoded a "warm orange-brown"
theme purely from `docs/3.`'s narrative description. The user then supplied a working HTML
prototype (`docs/ECM Planning Prototype.dc.html`) with a different, concrete navy/gold theme
and a 13-screen nav that doesn't exactly match `docs/6.`'s 15-module list.
What happened: Had the prototype been checked first, the initial design system would have
been correct on the first pass instead of needing a correction (ADR-0006).
Root cause: Text specs describe *intent*; a working prototype is *ground truth* and can
diverge from earlier prose as the product evolves. We designed from the older artifact only.
Rule: When both a prose doc and a working prototype/mockup exist for the same subject, always
open and defer to the prototype — treat prose docs as historical intent, not current spec.
When a new prototype/mockup file appears in the repo, proactively diff it against the current
design system and knowledge base before doing unrelated work.

## 2026-07-27 — Parallel po-analyst/domain-expert passes diverged on schema shape
Context: Detailed phase-plan expansion (docs/9. §11 open items → docs/10.). `po-analyst` and
`domain-expert` were run in parallel against the same five human decisions, each without seeing
the other's artifact.
What happened: On the two questions that were genuinely data-model-shaped (EAC variant scope,
approval-authority model), both agents independently proposed reasonable but incompatible
shapes — `po-analyst` scoped a 3-value EAC enum and a role-only boolean approval matrix;
`domain-expert` (unaware of those choices) recommended a 5-variant PF engine and an amount-tiered
policy model with different field/table names. `system-architect` reconciled both correctly
(ADR-0007, ADR-0008), but that reconciliation was an extra full agent pass that sequencing or
tighter prompts would have avoided.
Root cause: Domain formulas/rules usually *drive* what shape a schema needs, so running the
schema-adjacent agent (po-analyst) before the domain agent has finished loses the dependency; and
neither prompt asked agents to keep schema proposals directional-only.
Rule: For a planning task where a decision implies new entities/enums/field names, either (a) run
`domain-expert` before `po-analyst` (domain rules first, since they constrain the schema), or (b)
if they must run in parallel, instruct both explicitly to keep schema proposals minimal/directional
and defer concrete field naming to `system-architect`. Promoted to `CLAUDE.md` Learned Rules.

## 2026-07-27 — Validation must run from repo root or with explicit absolute paths
Context: Solution validation after implementation of master-plan design (docs/specs/master-plan/).
Repository already physically separated (src/, web/, infra/, tests/, docs/).
What happened: `dotnet test` run from `web/` directory (PowerShell CWD) failed to find the solution
or projects, causing a false start. Re-running from repo root with `dotnet test .\CMPlus.sln`
succeeded immediately. Validation confirmed: dotnet build + dotnet test pass; only non-blocking
warnings remain (NU1903 CVE in OpenApi 2.0.0, xUnit discovery for empty test projects).
Root cause: Relative paths in dotnet commands resolve from CWD. `web/` is the frontend folder and
lacks .sln context; PowerShell had navigated there during frontend work, breaking backend validation.
Rule: When running solution-wide validation commands (dotnet build/test/restore), always run from
repo root or use explicit absolute paths. Before reporting a build/test failure, verify CWD matches
the expected location for the command.

## 2026-07-27 — "Restructure" requests may already be physically satisfied
Context: User request to "restructure the repository" for backend/frontend separation. Inspection
revealed src/, web/, infra/, tests/, docs/ folders already existed with proper separation.
What happened: The requested structure was already in place; the task was validation and wiring
checks (solution references, build scripts), not a physical file reorganization.
Root cause: "Restructure" is ambiguous — can mean physical file layout OR logical wiring (imports,
solution structure, build config). We assumed the former before checking repo state.
Rule: When asked to "restructure" or "reorganize" a repository, first scan the actual folder
structure. If physical separation already matches the request, reframe the task as "validate and
wire the existing structure" instead of moving files. Report the existing layout and ask if the
user means logical wiring, build config, or something else.

## 2026-08-09 — UUIDv7 is time-ordered only to the millisecond; never sort by it for "newest first"
Context: `ListPaymentCertificatesQuery` (Sprint 9 read-side) needed "newest first" ordering. The
entity base uses `Guid.CreateVersion7()`, whose whole selling point is being time-ordered, so
`OrderByDescending(c => c.Id)` looked like the obvious free win.
What happened: `backend-developer` empirically probed it before trusting it and found ~80% of
triples created in a tight loop came back in the wrong order under every comparison strategy tried.
Root cause: UUIDv7's timestamp has **millisecond** resolution and .NET's `CreateVersion7()` adds no
monotonic counter within a millisecond — the remaining bits are random. Anything created in the
same millisecond (bulk import, seeding, a fast loop, a busy endpoint) sorts arbitrarily. The
"time-ordered UUID" name promises more than the implementation delivers at sub-millisecond scale.
Additional trap layered on top: sorting Guids **in SQL Server** uses `uniqueidentifier` collation,
which does not compare bytes left-to-right, so pushing such an `OrderBy` to the database is wrong
in a second, independent way.
Rule: Never order by a UUIDv7 primary key to mean "creation order." Add an explicit `CreatedAt
DateTimeOffset` column and sort on that (with the id as a deterministic tie-break if needed). If a
Guid must be compared for ordering at all, do it client-side on the canonical hex form, never via
SQL Server's `uniqueidentifier` collation. Applies to every entity in this codebase, since they all
inherit `Entity`'s `Guid.CreateVersion7()` id.

## 2026-08-10 — EF Core InMemory does not roll back a failed SaveChanges
Context: Sprint 10's H-03 fix added a `rowversion` concurrency token to `Project`. Verifying it,
`security-auditor` isolated a property of the test substitute that invalidates a whole class of
assertion this repo relies on.
What happened: when a `SaveChanges` fails with `DbUpdateConcurrencyException` under EF Core
InMemory, **other entities staged in that same `SaveChanges` still persist**. Only the conflicting
row is left alone. Concretely, the losing approver's `ApprovalAction` row survived a failed
approval (2 → 3 rows), so a retry by that same approver then tripped `DuplicateChainVoter` and the
document stranded — a failure mode that does not exist on SQL Server, where the implicit
per-`SaveChanges` transaction rolls the whole batch back.
Root cause: InMemory has no transaction support at all (`BeginTransactionAsync` throws outright),
so there is nothing to roll back. This is an environment artifact, not a production defect.
Rule: **Never assert "a failed transition wrote nothing" on the concurrency path under InMemory** —
such a test is unsound in both directions: it can pass while production would differ, and it can
fail while production is correct. Guard paths that return *before* staging anything remain sound,
because nothing was staged. Any post-conflict state assertion must be deferred to a real SQL Server
run and labelled as such. More generally: this is the second time InMemory's silence about a
relational guarantee produced a false result (the first was unique indexes, which it ignores
entirely — see the `ApprovalPolicy` filtered-index question). Treat "InMemory says it's fine" as
evidence about the C# logic only, never about the storage engine's guarantees.
*(Update 2026-08-11: the predicted third instance landed — see ADR-0021 and the entry below.)*

## 2026-08-09 — A fully green suite is not evidence of correctness; only adversarial verification is
Context: Across Sprints 9, 10 and 12, `qa-engineer`/`security-auditor` repeatedly stopped trusting
"all tests pass" and instead mutated the code under test, probed with a canary fixture, or
re-derived the expected value independently. Every single time, that adversarial pass — never the
pass count — is what surfaced a real, sometimes severe, defect sitting underneath a green suite.
What happened, six verified instances:
1. **H-01** (sprint-09.md): `PinnedApprovalChainResolver` re-derived approval-step authority from a
   policy's *entire* rule set instead of the amount band that actually routed the document. An 822-
   test suite passed straight over it because "the whole existing suite uses policies whose duplicate
   `StepNo`s carry the same role" — no fixture ever had two different roles share a `StepNo` across
   bands. Execution-verified impact: a QS cleared a step the tenant's own DoA reserved for a PM on a
   real ฿5,000,000 certificate, and the `ApprovalAction` evidence row recorded it as a valid approval.
2. **H-02** (sprint-09.md): `QuorumCount` was accepted by the write API, persisted, and echoed back —
   and enforced nowhere. A dual-control step cleared on one signature. Nothing in the green suite
   exercised the write-API/engine boundary together.
3. **`ThresholdEndPct` branch** (sprint-09.md §"Verification-quality note"): `qa-engineer` deleted
   the branch in `AdvanceRecoveryCalculator` as a canary mutation and all 24 existing tests kept
   passing — the branch was provably unobservable by the suite's own parameter choices.
4. **VO/Payment approval-history endpoint 404s unconditionally** (found by `frontend-developer`
   consuming it, documented in `GetApprovalActionHistoryQueryHandler.cs`'s own "Lesson worth keeping"
   remark): the route, controller and shared DTO were all real and shipped; a `_ => false` default
   arm for a not-yet-added document type made every request 404 — indistinguishable from a legitimate
   not-found, so no backend test noticed and nothing failed to compile.
5. **EXIF scrubber stops at the first SOS marker** (sprint-12.md H-01): the code-level tests asserted
   on the scrubber's return value; only reading the *stored bytes back off disk* after a real
   `UploadPhotoCommandHandler` run proved GPS/IMEI/`<script>` payloads placed after the first scan
   segment survived untouched.
6. **Offline photo outbox quarantine sweep** (sprint-12.md N-02): a read-modify-write over a stale
   `storage.list()` snapshot could revert an in-flight `markSynced` if the sweep raced a mid-flush
   logout, leaving a record at `status=syncing` with its `blob` already nulled out — permanently
   uncompletable, indistinguishable in the UI from one still genuinely in progress, until the next
   login's reconciliation flipped it to a misleading `failed`.
Root cause: a passing test proves only that the assertions it happens to make are satisfied. None of
these suites were badly written by the standards they were written to — they were simply never
adversarial. A fixture that always uses the same role per band, or the same-tenant happy path, or
reads a return value instead of the disk, cannot detect the class of bug that only shows up when that
one assumption is violated.
Rule: **Never treat a pass count as proof a guard, invariant, or fix works.** Before trusting a
guard: (a) mutate the implementation (delete/invert the branch) and confirm the test suite notices;
(b) assert on the durable artifact — stored bytes, row counts, the persisted value — never only the
in-memory return value or a mocked boundary; (c) add at least one fixture where the "obvious"
shortcut (same role, same tenant, same tier) does *not* hold. `qa-engineer` and `security-auditor`
own this discipline explicitly; `backend-developer`/`frontend-developer` should self-apply it to any
security- or money-moving guard before declaring it done. Promoted to `CLAUDE.md` Learned Rules.

## 2026-08-11 — A filtered unique index does nothing for its NULL-discriminator group
Context: ADR-0008 established "one active row per scope, enforced by a filtered unique index" for
`ApprovalPolicy`; Sprint 14's Baseline work reused the same pattern. QA and `backend-developer`
verified both on SQLite (Docker/SQL Server unavailable) rather than trusting EF Core InMemory, per
the 2026-08-10 lesson above, and found two distinct, real defects — full detail and fixes in
**ADR-0021**.
What happened: (1) `ApprovalPolicy.ProjectId` is nullable and every policy the shipped code could
create had `ProjectId = null` (tenant-wide default). Standard ANSI/SQL-Server semantics treat
`NULL ≠ NULL`, so the filtered index `WHERE IsActive = 1` **never fires** when two competing rows
both have `ProjectId = null` — two concurrent policy updates could both succeed, leaving two
simultaneously-active policy versions with no exception raised anywhere. (2) Separately,
`ActivateBaselineCommandHandler` loaded `target` before `previousActive`, so EF's change-tracking
order could emit the *activate* UPDATE before the *deactivate*, momentarily violating the (non-
nullable, otherwise-correct) `Baseline` filtered index — reproduced ~30–50% of trials on SQL
Server/SQLite, invisible in every InMemory run because InMemory ignores the index entirely.
Root cause: (1) is a NULL-semantics gap in the unique-index mechanism itself, not a bug in
application code — nothing could have caught it by code review alone. (2) is EF's statement-batch
ordering not matching the invariant the index enforces; InMemory cannot surface it because it has no
concept of the index at all.
Rule: **A filtered unique index on a nullable column silently does not constrain the NULL group —
split it into two indexes (one for the NULL case, one for the non-NULL case) rather than relying on
one.** Never assume EF's tracked-entity save order matches transaction-safe ordering when a filtered
unique index is involved — either force ordering with two sequential `SaveChanges` calls inside one
transaction, or don't rely on statement order at all. Both classes of defect are provably invisible
under EF Core InMemory; verify single-active/uniqueness invariants on SQLite or a real relational
provider, never InMemory alone. See ADR-0021 for the concrete fix (two disjoint filtered indexes) and
the pre-flight dedup-check migration pattern for deploying it safely onto data that may already be
corrupted.

## 2026-08-11 — "Append-only, no mutator" in a doc comment is not immutability
Context: Several entities (`ApprovalAction`, `PaymentCertificateApprovalStep`,
`ActivityProgressLog`, `ProjectFinanceLedger`, `EvmPeriodSnapshot`, `CpmRun` and others) carry doc
comments claiming they are append-only. `security-auditor` proved by execution, more than once
(Sprint 9 M-01 on `PaymentCertificateApprovalStep`; repeated on later entities), that an ordinary
`DbContext` could still rewrite or delete the row — "no setter in the public API" restricts the
*generated* code paths, not a raw `Update`/`Remove` call through the same `DbContext`.
What happened: the fix that emerged and then got reused is now the standing pattern —
`IAppendOnly` (a marker interface) plus `AppendOnlyGuardInterceptor` (a `SavingChanges`
interceptor) rejects `EntityState.Modified`/`Deleted` for anything implementing it, and the
narrower `INeverModified` (added for snapshot-shaped entities that legitimately need
delete-as-a-set, e.g. `ReturnForRevision` clearing and rebuilding approval steps, but never a field
edit) blocks only `Modified`. Both are now applied to well over a dozen entities across the ledger,
audit, snapshot and approval-history families.
Root cause: C# access control (no public setter) is a compile-time convenience, not a runtime
guarantee — nothing stops a handler with direct `DbContext` access from tracking the entity as
`Modified`/`Deleted` regardless of what its properties expose.
Rule: **"The entity has no setter" is not immutability.** Claims of append-only or never-modified
semantics must be enforced structurally at `SavingChanges`, not by convention or doc comment.
Reuse `IAppendOnly`/`INeverModified` + `AppendOnlyGuardInterceptor` for any new entity with this
shape rather than re-deriving the pattern; promoted into the `/clean-architecture-dotnet` skill.

## 2026-08-11 — Touching the shared integration-test fixture's DbContext registration broke 188 of 544 tests
Context: `S14-BE-01`'s Baseline single-active-activation fix needed a transaction wrapper. The
`CpmScheduleRepository.SaveResultsAsync` precedent (and `RecalculateCpmCommandHandlerCpmRunCaptureTests`,
which suppresses `InMemoryEventId.TransactionIgnoredWarning` locally) suggested doing the same:
unconditionally open a transaction and add a matching `ConfigureWarnings` ignore to
`CustomWebApplicationFactory`'s shared `CmPlusDbContext` registration — the ONE registration every
WebApi integration test class in the solution reuses.
What happened: that change was built and run, and reproducibly broke 188 of the Integration suite's
544 tests — spanning dozens of unrelated feature test classes (Import, WbsNodes, CashFlow,
ActualCosts, ...) — deterministically across repeated clean rebuilds, including with xUnit
parallelization forced off (so not a parallel-execution race). Removing only that one
`ConfigureWarnings` line restored the suite to its expected 2 pre-fix failures every time. The exact
EF Core internal mechanism was not fully root-caused (suspected interaction with EF's internal
service-provider or compiled-model caching), but the effect was certain and repeatable.
Root cause: `CustomWebApplicationFactory`'s `CmPlusDbContext` registration is a single shared entry
point reused across the whole Integration suite; a change scoped to "make one InMemory warning go
away for one new caller" landed on infrastructure every other test class also depends on.
Rule: **Never modify the shared `CustomWebApplicationFactory`/`DbContext` registration to satisfy
one test's needs.** Prefer (a) guarding the production code path on `Database.IsRelational()` so
InMemory skips the transaction wrapper entirely instead of opening one and suppressing the warning
about it doing nothing (this was also the fix actually shipped — see `BaselineRepository
.TryActivateAsync`), or (b) if a test genuinely needs different factory configuration, use
`WithWebHostBuilder(...)` from inside that test class to layer config on a *cloned* factory (see
`LoginRateLimiterTests`/`FallbackPolicyTests`), never mutate the shared registration in place.

## 2026-08-10 — Escalate domain-authority-changing ambiguities with worked fixtures; don't quietly pick one
Context: Five separate questions this session changed money, time, or approval authority in a way
that could not be derived unambiguously from existing docs or code: VO escalation numerator/
denominator/reset semantics (ADR-0015), whether quorum binds rejection (ADR-0016), whether an
ordinary BAC edit is legal once a VO is approved (ADR-0017), and the EOT evaluator's
contemporaneous-CPM and absolute-counting readings (ADR-0019, ADR-0020). Each had at least two
textually defensible readings that produced materially different, hard-to-reverse behaviour (ADR-
0015's own fixture alone shows three readings 9.73%/13.03%/11.38% on identical data).
What happened: instead of picking the most-defensible-looking reading and shipping it,
`domain-expert` escalated each with a precise question, the competing readings, external reference
points (FIDIC clauses, Thai statute, AACE/SCL protocols), and — critically — worked fixtures showing
exactly how the readings diverge on real numbers. The human decided each one in a single pass with
no rework.
Root cause: none — this is the practice worth keeping, not a failure being corrected.
Rule: **When a domain rule's reading changes money, time, or authority and cannot be derived
unambiguously from the code or docs, escalate to the human with a precise question and worked
fixtures showing how the answers diverge — never silently pick one and present it as settled.**
Promoted to `CLAUDE.md` Learned Rules.

## 2026-08-10 — A backlog acceptance criterion mislabeled a domain metric; verify against the standard, not the AC text
Context: `backlog-detailed.md` US-12.2's own acceptance criterion defined the Man/Equipment
"Productivity Index" as `ManCount / PlannedManCount` (actual staffing over planned staffing).
`domain-expert`, specced against the actual meaning of labour productivity (earned man-hours over
actual man-hours, the man-hour analogue of EV/AC), caught that this is a **manning ratio**, not a
productivity index, and that the two disagree in sign on realistic data (fixture M-02: manning ratio
1.25 reads "better than plan" in green while the true PI of 0.60 reads red on the same day —
overstaffed and still behind).
What happened: had the AC been implemented as literally written, the shipped feature would have
told a PM the opposite of the truth on any day where headcount and output diverge — exactly the days
the metric exists to catch.
Root cause: a backlog acceptance criterion is written by whoever drafted the story, and can encode a
plausible-sounding but wrong definition of a domain term; it is not itself an authority on domain
correctness.
Rule: When an acceptance criterion or DoD asserts a formula or metric definition, verify it against
the actual domain standard (here: earned value's man-hour analogue) before implementing it, the same
way `evm-formulas.md`/`actual-cost.md` are treated as authoritative over prose descriptions
elsewhere. Do not implement a metric's definition straight from backlog text without this check.

## 2026-08-12 — "Gated/done" is a claim to re-test, not a conclusion; look one layer harder before declaring completion
Context: Late in a long build session, after the planned sprint work was exhausted, I repeatedly
concluded the remaining backlog was "environment-gated or needs a human decision" and moved to
declare completion. The human kept issuing a bare "continue" each time.
What happened: every time I pushed past the "we're done" instinct and looked again, a genuine,
non-gated, fixable-here item was actually there — eight of them in one segment: the `/health/ready`
DB readiness check (I had wrongly filed it as Docker-blocked; a fake `IDatabaseConnectivityProbe`
returning false makes the 503 path fully testable with no Docker), the L-08 npm-audit cleanup, the
L-03 path-traversal case-sensitivity fix, the L-01 login timing oracle, the N-06 fail-closed routing
fix, N-03 verification, the README un-freeze, and the N-08 doc-comment drift — each landed with
tests and a green build. None required the environment I had claimed blocked them.
Root cause: "gated" and "done" are comfortable stopping points that a tired session reaches for; several
items were mis-classified as gated on a first, shallow read (the `/health/ready` case is the clearest
— the *deploy* is gated, the *unit-testable 503 branch* is not). The distinction that matters is
**"can I build AND verify a slice of this here?"**, not "is the whole feature deployable here?".
Rule: Before declaring a backlog "gated" or "complete", separate each item into the part that needs
the missing environment/decision and the part that does not, and do the part that does not. A guard's
unit-testable branch, a doc that's wrong, a config gap, an audit finding — these are almost never
truly gated even when the end-to-end feature is. Treat "I think we're done" as a prompt to re-scan at
one finer grain, not as a conclusion.

## 2026-08-12 — Doc comments that cite a removed invariant are latent traps; re-verify them when an invariant tightens
Context: `ApprovalRoutingModels.cs` documented the VO-escalation denominator as
`Project.EscalationBaselineContractValue (OriginalContractValue ?? ContractValue)`. A later hardening
(Sprint-10 H-02) had made `OriginalContractValue` NOT NULL and **removed** the `?? ContractValue`
fallback specifically because a nullable baseline silently degrading to the live, VO-inflated value
reinstates the self-diluting-denominator defect the rename exists to prevent. The comment still
described the old, dangerous fallback. A Sprint-10 migration comment similarly justified a backfill
with "no VariationOrder can exist yet" when in fact an earlier migration already creates that table —
the backfill is safe for a *different* reason (one-batch apply to a fresh DB before any app code runs).
What happened: no runtime bug (the code was correct; only the comments lied), but a future caller or
reviewer reading either comment would draw a materially wrong conclusion about a money-moving control —
exactly the audience these comments exist to protect.
Root cause: when an invariant is tightened (nullable→NOT NULL, fallback removed), the code is updated
and tested but the prose explaining *why* is not part of the compiler's or the test suite's view, so it
rots silently and points at the pre-hardening behaviour.
Rule: When a change removes a fallback or tightens a nullability/uniqueness invariant, grep for prose
that describes the old behaviour (the removed operator, the old "can be null", the old default) and fix
it in the same change. A comment that explains a safety-critical *why* is part of the guard; leave it
stale and you have documented the exact defect you just closed as if it were still the design.

## 2026-08-12 — Fail closed on "matched but unselectable", not just on "no match"; a permissive fallback must be reachable only from genuine emptiness
Context: `ApprovalRoutingService.Resolve` returns the permissive hard-coded `FallbackApprovalChain`
(single ProjectDirector step, self-approval false) when a tenant has **no** policy configured for a
document type — the intended "restrictive, not permissive" §5.3-step-6 behaviour. But the same
fallback was also reached when candidate policies *existed* yet `SelectPolicy` returned null (e.g. a
project-scoped override for a different project with no tenant-level default) — i.e. a misconfigured
policy set silently got the permissive default instead of being blocked.
What happened (N-06): the reachable shape on the shipped Submit path is narrow (the reader pre-filters
inactive policies, so "all deactivated" collapses to genuinely-empty before `Resolve`), but the
non-empty-yet-unselectable case is real and routed a document through a control the tenant had actually
configured *away from*. Fix: `Resolve` now returns `Failure(PolicyGap)` (HTTP 422, submission blocked)
when `CandidatePolicies.Count > 0` but selection yields null; the permissive fallback survives only for
a genuinely empty candidate set. Proven by mutation (broke the guard, saw red, reverted).
Root cause: "no applicable policy" was collapsed into one branch, but it has two very different
meanings — *nothing was configured* (fall back to the safe default) versus *something was configured
and none of it applies here* (a misconfiguration; block and make a human fix it). Treating them alike
turns a config error into a silent authority downgrade.
Rule: A permissive/lenient fallback must be reachable **only** from a provably-empty input, never from
"input existed but nothing selected". Distinguish "empty" from "non-empty-but-no-match" and fail closed
on the latter for any authority/money/security control. See ADR-0008 approval engine and N-06.
